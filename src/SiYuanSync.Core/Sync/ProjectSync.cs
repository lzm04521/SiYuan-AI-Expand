using System.Text;
using Microsoft.Extensions.Logging;
using SiYuanSync.Core.Models;
using SiYuanSync.Core.Siyuan;
using SiYuanSync.Core.State;

namespace SiYuanSync.Core.Sync;

public static class ProjectSync
{
    public static async Task<ProjectRunResult> RunAsync(
        ProjectConfig project, ISiyuanClient siyuan, IStateStore state,
        ILogger logger, CancellationToken ct)
    {
        var files = new List<FileResult>();
        int ok = 0, skipped = 0, failed = 0;

        // 1. docPath
        string docPath;
        try { docPath = PathNormalizer.NormalizeDocPath(project.DocPath); }
        catch (PathNormalizerException e)
        { return Failed(project, $"docPath 无效：{e.Message}", files, 0, 0, 0); }

        // 2. notebook
        IReadOnlyList<Notebook> notebooks;
        try { notebooks = await siyuan.ListNotebooksAsync(ct); }
        catch (SiyuanAuthException e) { return Failed(project, $"鉴权失败：{e.Message}", files, 0, 0, 0); }
        catch (Exception e) { return Failed(project, $"获取笔记本失败：{e.Message}", files, 0, 0, 0); }

        var nb = notebooks.FirstOrDefault(n => n.Name == project.Notebook);
        if (nb is null) return Failed(project, $"笔记本 '{project.Notebook}' 不存在或已关闭", files, 0, 0, 0);

        // 3. parentPath 规范化 + 存在校验
        string parentPath;
        try { parentPath = PathNormalizer.NormalizeParentPath(project.ParentPath); }
        catch (PathNormalizerException e)
        { return Failed(project, $"parentPath 无效：{e.Message}", files, 0, 0, 0); }

        IReadOnlyList<string> parentIds;
        try { parentIds = await siyuan.GetDocIdsByHPathAsync(nb.Id, parentPath, ct); }
        catch (SiyuanAuthException e) { return Failed(project, $"鉴权失败：{e.Message}", files, 0, 0, 0); }
        catch (Exception e) { return Failed(project, $"校验父目录失败：{e.Message}", files, 0, 0, 0); }

        if (parentIds.Count == 0)
            return Failed(project, $"思源中父目录 '{parentPath}' 不存在，请先点[同步创建父目录]", files, 0, 0, 0);

        // 4. 扫描
        ScanResult scan;
        try { scan = DocScanner.Scan(docPath); }
        catch (Exception e) { return Failed(project, $"扫描目录失败：{e.Message}", files, 0, 0, 0); }

        foreach (var se in scan.Errors)
        {
            files.Add(new FileResult(se.Path, FileOutcome.Failed, se.Reason));
            failed++;
        }

        var presentRels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 5. 逐文件
        foreach (var sf in scan.Files)
        {
            ct.ThrowIfCancellationRequested();
            string rel = sf.RelPath;
            presentRels.Add(rel);
            try
            {
                string hpath;
                try { hpath = PathNormalizer.RelPathToHPath(parentPath, rel); }
                catch (PathNormalizerException e)
                { files.Add(new FileResult(rel, FileOutcome.Failed, e.Message)); failed++; continue; }

                string raw;
                try { raw = await File.ReadAllTextAsync(sf.AbsolutePath, Encoding.UTF8, ct); }
                catch (Exception e) { files.Add(new FileResult(rel, FileOutcome.Failed, $"读取失败：{e.Message}")); failed++; continue; }

                var proc = ContentPreprocessor.Process(raw);
                var title = string.IsNullOrEmpty(proc.Title) ? Path.GetFileNameWithoutExtension(sf.AbsolutePath) : proc.Title;

                // 思源端文档可能被手动删除：先查存在性，不存在则不比对 hash 直接重推；存在且内容未变才跳过
                IReadOnlyList<string> existingIds;
                try { existingIds = await siyuan.GetDocIdsByHPathAsync(nb.Id, hpath, ct); }
                catch (Exception e) when (e is not OperationCanceledException and not SiyuanAuthException)
                { files.Add(new FileResult(rel, FileOutcome.Failed, $"检查思源端文档存在性失败：{e.Message}")); failed++; continue; }

                var hash = ContentPreprocessor.ComputeHash(proc.BodyMd);
                if (existingIds.Count > 0 && state.GetHash(project.Name, rel) == hash)
                { files.Add(new FileResult(rel, FileOutcome.Skipped, null)); skipped++; continue; }

                UpsertResult upsert;
                try { upsert = await DocUpsert.UpsertAsync(siyuan, nb.Id, hpath, proc.BodyMd, title, ct); }
                catch (SiyuanAuthException) { throw; } // 让外层停项目
                catch (DocUpsertException e)
                { files.Add(new FileResult(rel, FileOutcome.Failed, $"{e.Stage} 失败：{e.InnerException?.Message}")); failed++; continue; }

                try { state.RecordFileSync(project.Name, rel, hash, upsert.DocId, DateTime.UtcNow); }
                catch (Exception e)
                { files.Add(new FileResult(rel, FileOutcome.Failed, $"状态写入失败（思源已成功，下轮将重推）：{e.Message}")); failed++; continue; }

                // 记录成功方式：新建 vs 更新（Rebuilt 删旧重建也归为更新），供同步日志展示
                files.Add(new FileResult(rel,
                    upsert.Mode == UpsertMode.Created ? FileOutcome.Created : FileOutcome.Updated, null));
                ok++;
            }
            catch (SiyuanAuthException e)
            {
                files.Add(new FileResult(rel, FileOutcome.Failed, $"鉴权失败，停止本项目后续文件：{e.Message}"));
                failed++;
                return Failed(project, $"鉴权失败：{e.Message}", files, ok, skipped, failed);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                files.Add(new FileResult(rel, FileOutcome.Failed, e.Message));
                failed++;
            }
        }

        // 6. 本地删除：思源保留，状态清掉
        try { PurgeMissing(project, state, presentRels); }
        catch (Exception e) { logger.LogWarning(e, "清理本地已删除文件状态失败：{Project}", project.Name); }

        // 7. 按配置设置父文档下子文档排序方式（等价于思源右键父文档→排序）；失败不影响同步结果
        if (project.SortMode is int sortMode)
        {
            try { await siyuan.SetDocSortModeAsync(parentIds[0], sortMode, ct); }
            catch (Exception e) when (e is not OperationCanceledException)
            { logger.LogWarning(e, "设置父文档排序方式失败（思源版本可能低于 v3.8.1）：{Project} sortMode={SortMode}", project.Name, sortMode); }
        }

        var status = failed > 0 ? (ok > 0 ? RunStatus.Partial : RunStatus.Failed) : RunStatus.Success;
        return new ProjectRunResult(project.Name, status, ok, skipped, failed, files, null);
    }

    private static void PurgeMissing(ProjectConfig project, IStateStore state, HashSet<string> present)
    {
        // StateStore 提供 ListRelsByProject（在 Task 7 接口补一个方法，见下）
        foreach (var rel in state.ListRelsByProject(project.Name))
            if (!present.Contains(rel))
                state.DeleteFileSync(project.Name, rel);
    }

    private static ProjectRunResult Failed(ProjectConfig p, string error, List<FileResult> files, int ok, int skipped, int failed) =>
        new(p.Name, RunStatus.Failed, ok, skipped, failed, files, error);
}
