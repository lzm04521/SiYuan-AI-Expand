using SiYuanSync.Core.Models;
using SiYuanSync.Core.Siyuan;

namespace SiYuanSync.Core.Sync;

public enum ParentInitStatus { Created, Exists, Failed }
public sealed record ParentInitResult(string ProjectName, ParentInitStatus Status, string? DocId, string? Error);

/// <summary>在思源中按项目 parentPath 逐级创建缺失的父文档（已存在则跳过），供单项目/批量创建父目录端点共用。</summary>
public static class ParentDocInitializer
{
    /// <summary>单项目：解析笔记本（项目 notebook 为空回退 defaultNotebook）→ 已存在返回 Exists → 否则逐级补建缺失层级。
    /// 仅将可预期失败（parentPath 无效、笔记本不存在）记为 Failed；网络/认证/创建异常冒泡，由调用方定义策略。</summary>
    public static async Task<ParentInitResult> EnsureAsync(
        ProjectConfig project, string defaultNotebook, ISiyuanClient siyuan, CancellationToken ct)
    {
        string parentPath;
        try { parentPath = PathNormalizer.NormalizeParentPath(project.ParentPath); }
        catch (PathNormalizerException e)
        { return new(project.Name, ParentInitStatus.Failed, null, $"parentPath 无效：{e.Message}"); }

        var notebooks = await siyuan.ListNotebooksAsync(ct);
        var nbName = string.IsNullOrWhiteSpace(project.Notebook) ? defaultNotebook : project.Notebook;
        var nb = notebooks.FirstOrDefault(n => n.Name == nbName);
        if (nb is null)
            return new(project.Name, ParentInitStatus.Failed, null, $"笔记本 '{nbName}' 不存在或已关闭");

        var existing = await siyuan.GetDocIdsByHPathAsync(nb.Id, parentPath, ct);
        if (existing.Count > 0)
            return new(project.Name, ParentInitStatus.Exists, existing[0], null);

        // 逐级创建：createDocWithMd 对中间层级是否自动补建不确定，逐段查存在、缺失即建
        var segments = parentPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var path = "";
        string createdId = "";
        foreach (var seg in segments)
        {
            path += "/" + seg;
            var ids = await siyuan.GetDocIdsByHPathAsync(nb.Id, path, ct);
            if (ids.Count == 0)
                createdId = await siyuan.CreateDocWithMdAsync(nb.Id, path, "", ct);
        }
        return new(project.Name, ParentInitStatus.Created, createdId, null);
    }

    /// <summary>批量：逐项目串行，单项非认证失败记 Failed 继续后续项目（隔离）；SiyuanAuthException 冒泡
    /// （token 无效是全局错误，且必然在首个项目触发，不存在执行一半的问题）。</summary>
    public static async Task<IReadOnlyList<ParentInitResult>> EnsureAllAsync(
        IReadOnlyList<ProjectConfig> projects, string defaultNotebook, ISiyuanClient siyuan, CancellationToken ct)
    {
        var results = new List<ParentInitResult>(projects.Count);
        foreach (var p in projects)
        {
            ct.ThrowIfCancellationRequested();
            try { results.Add(await EnsureAsync(p, defaultNotebook, siyuan, ct)); }
            catch (OperationCanceledException) { throw; }
            catch (SiyuanAuthException) { throw; }
            catch (Exception e)
            { results.Add(new(p.Name, ParentInitStatus.Failed, null, e.Message)); }
        }
        return results;
    }
}
