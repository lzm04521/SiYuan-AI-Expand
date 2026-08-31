using System.Text;
using System.Text.RegularExpressions;
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
        int ok = 0, skipped = 0, failed = 0, deleted = 0;

        // 1. docPath
        string docPath;
        try { docPath = PathNormalizer.NormalizeDocPath(project.DocPath); }
        catch (PathNormalizerException e)
        { return Failed(project, $"docPath 无效：{e.Message}", files, 0, 0, 0, 0); }

        // 2. notebook
        IReadOnlyList<Notebook> notebooks;
        try { notebooks = await siyuan.ListNotebooksAsync(ct); }
        catch (SiyuanAuthException e) { return Failed(project, $"鉴权失败：{e.Message}", files, 0, 0, 0, 0); }
        catch (Exception e) { return Failed(project, $"获取笔记本失败：{e.Message}", files, 0, 0, 0, 0); }

        var nb = notebooks.FirstOrDefault(n => n.Name == project.Notebook);
        if (nb is null) return Failed(project, $"笔记本 '{project.Notebook}' 不存在或已关闭", files, 0, 0, 0, 0);

        // 3. parentPath 规范化 + 存在校验
        string parentPath;
        try { parentPath = PathNormalizer.NormalizeParentPath(project.ParentPath); }
        catch (PathNormalizerException e)
        { return Failed(project, $"parentPath 无效：{e.Message}", files, 0, 0, 0, 0); }

        IReadOnlyList<string> parentIds;
        try { parentIds = await siyuan.GetDocIdsByHPathAsync(nb.Id, parentPath, ct); }
        catch (SiyuanAuthException e) { return Failed(project, $"鉴权失败：{e.Message}", files, 0, 0, 0, 0); }
        catch (Exception e) { return Failed(project, $"校验父目录失败：{e.Message}", files, 0, 0, 0, 0); }

        if (parentIds.Count == 0)
            return Failed(project, $"思源中父目录 '{parentPath}' 不存在，请先点[同步创建父目录]", files, 0, 0, 0, 0);

        // 4. 构造扫描过滤器（正则已在保存/加载期校验；手改 config 绕过校验时这里兜底 Failed）
        ScanFilter? filter;
        try
        {
            filter = new ScanFilter(project.SettleMinutes ?? 0,
                string.IsNullOrWhiteSpace(project.IncludePattern) ? null
                    : new Regex(project.IncludePattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
                string.IsNullOrWhiteSpace(project.ExcludePattern) ? null
                    : new Regex(project.ExcludePattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
        }
        catch (Exception e) when (e is RegexParseException or ArgumentOutOfRangeException)
        { return Failed(project, $"正则配置非法：{e.Message}", files, 0, 0, 0, 0); }

        ScanResult scan;
        try { scan = DocScanner.Scan(docPath, filter); }
        catch (Exception e) { return Failed(project, $"扫描目录失败：{e.Message}", files, 0, 0, 0, 0); }

        foreach (var se in scan.Errors)
        { files.Add(new FileResult(se.Path, FileOutcome.Failed, se.Reason)); failed++; }

        // 被过滤（正则）/未静默文件：Skipped 带原因，与"hash 未变"同一展示机制
        foreach (var fe in scan.Filtered.Concat(scan.Deferred))
        { files.Add(new FileResult(ToRel(fe.Path, docPath), FileOutcome.Skipped, fe.Reason)); skipped++; }

        // 5. 逐文件
        foreach (var sf in scan.Files)
        {
            ct.ThrowIfCancellationRequested();
            string rel = sf.RelPath;
            try
            {
                string hpath;
                try { hpath = PathNormalizer.RelPathToHPath(parentPath, rel); }
                catch (PathNormalizerException e)
                { files.Add(new FileResult(rel, FileOutcome.Failed, e.Message)); failed++; continue; }

                string raw;
                try { raw = await File.ReadAllTextAsync(sf.AbsolutePath, Encoding.UTF8, ct); }
                catch (Exception e) { files.Add(new FileResult(rel, FileOutcome.Failed, $"读取失败：{e.Message}")); failed++; continue; }

                // HTML 报告：先转 Markdown，之后与 md 管线完全一致（首行 H1 剥离、hash、upsert）
                if (SupportedFileTypes.IsHtml(rel))
                {
                    try { raw = HtmlPreprocessor.ToMarkdown(raw); }
                    catch (Exception e)
                    { files.Add(new FileResult(rel, FileOutcome.Failed, $"HTML 转换失败：{e.Message}")); failed++; continue; }
                }

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
                return Failed(project, $"鉴权失败：{e.Message}", files, ok, skipped, failed, deleted);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                files.Add(new FileResult(rel, FileOutcome.Failed, e.Message));
                failed++;
            }
        }

        // 6. 删除同步：state 有记录但本地消失的 rel；DeleteSync=false 仅清 state（现状），true 时删除思源文档
        try
        {
            var stateRels = state.ListRelsByProject(project.Name);

            // 扫描截断跳过（仅 DeleteSync=true）：枚举被吞错使 PresentRels 缺席子树，继续删除会违反"本地存在 ⇒ 绝不删除"
            if (project.DeleteSync && scan.ScanTruncated)
                return Failed(project, "扫描被截断（目录枚举失败），已跳过删除同步", files, ok, skipped, failed, deleted);

            // 空扫描熔断（仅 DeleteSync=true）：防 docPath 配错/盘未挂载导致整批误删
            if (project.DeleteSync && stateRels.Count > 0 && scan.PresentRels.Count == 0)
                return Failed(project,
                    $"扫描到 0 个文件但历史有 {stateRels.Count} 条同步记录，疑似 docPath 异常，已跳过删除同步",
                    files, ok, skipped, failed, deleted);

            // 豁免集：本轮将同步（Files）或终将同步（Deferred）的 hpath——旧文档由新文件接管，不删
            var keepHPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sf in scan.Files)
                try { keepHPaths.Add(PathNormalizer.RelPathToHPath(parentPath, sf.RelPath)); }
                catch (PathNormalizerException) { }
            foreach (var d in scan.Deferred)
                try { keepHPaths.Add(PathNormalizer.RelPathToHPath(parentPath, ToRel(d.Path, docPath))); }
                catch (PathNormalizerException) { }

            foreach (var rel in stateRels)
            {
                ct.ThrowIfCancellationRequested();
                if (scan.PresentRels.Contains(rel)) continue; // 本地仍存在（含被过滤/静默/冲突）：不动

                if (!project.DeleteSync) { state.DeleteFileSync(project.Name, rel); continue; } // 现状：仅清 state

                string hpath;
                try { hpath = PathNormalizer.RelPathToHPath(parentPath, rel); }
                catch (PathNormalizerException e)
                { files.Add(new FileResult(rel, FileOutcome.Failed, $"删除候选 hpath 无效：{e.Message}")); failed++; continue; }

                if (keepHPaths.Contains(hpath))
                {   // 跨后缀改名等：文档由新文件接管，跳过删除、仅清 state（仅出现一次，透明记录）
                    state.DeleteFileSync(project.Name, rel);
                    files.Add(new FileResult(rel, FileOutcome.Skipped, "已由同 hpath 新文件接管"));
                    skipped++;
                    continue;
                }

                IReadOnlyList<string> ids;
                try { ids = await siyuan.GetDocIdsByHPathAsync(nb.Id, hpath, ct); }
                catch (SiyuanAuthException e) { return Failed(project, $"鉴权失败：{e.Message}", files, ok, skipped, failed, deleted); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                { files.Add(new FileResult(rel, FileOutcome.Failed, $"检查思源端文档失败：{e.Message}")); failed++; continue; }

                // 思源端查不到（已被手动删除）：无需删除调用，清 state 并记 Deleted
                var delFailed = false;
                foreach (var id in ids)
                {
                    try { await siyuan.RemoveDocByIdAsync(id, ct); }
                    catch (SiyuanAuthException e) { return Failed(project, $"鉴权失败：{e.Message}", files, ok, skipped, failed, deleted); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e)
                    { files.Add(new FileResult(rel, FileOutcome.Failed, $"删除思源文档失败：{e.Message}")); failed++; delFailed = true; break; }
                }
                if (delFailed) continue; // state 保留，下轮重试

                state.DeleteFileSync(project.Name, rel);
                files.Add(new FileResult(rel, FileOutcome.Deleted, null));
                deleted++;
            }
        }
        catch (SiyuanAuthException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { logger.LogWarning(e, "删除同步阶段异常：{Project}", project.Name); }

        // 7. 按配置设置父文档下子文档排序方式（等价于思源右键父文档→排序）；失败不影响同步结果
        if (project.SortMode is int sortMode)
        {
            try { await siyuan.SetDocSortModeAsync(parentIds[0], sortMode, ct); }
            catch (Exception e) when (e is not OperationCanceledException)
            { logger.LogWarning(e, "设置父文档排序方式失败（思源版本可能低于 v3.8.1）：{Project} sortMode={SortMode}", project.Name, sortMode); }
        }

        var status = failed > 0 ? (ok > 0 ? RunStatus.Partial : RunStatus.Failed) : RunStatus.Success;
        return new ProjectRunResult(project.Name, status, ok, skipped, failed, deleted, files, null);
    }

    private static string ToRel(string abs, string docPath) => Path.GetRelativePath(docPath, abs).Replace('\\', '/');

    private static ProjectRunResult Failed(ProjectConfig p, string error, List<FileResult> files, int ok, int skipped, int failed, int deleted) =>
        new(p.Name, RunStatus.Failed, ok, skipped, failed, deleted, files, error);
}
