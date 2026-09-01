using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SiYuanSync.App.Web.Errors;
using SiYuanSync.Core.Config;
using SiYuanSync.Core.Models;
using SiYuanSync.Core.Siyuan;
using SiYuanSync.Core.Sync;

namespace SiYuanSync.App.Web.Endpoints;

/// <summary>POST /api/projects/set-enabled 请求体。</summary>
public sealed class SetEnabledRequest
{
    public string[]? Names { get; set; }
    public bool Enabled { get; set; }
}

public static class ProjectEndpoints
{
    public static void Map(IEndpointRouteBuilder app, ConfigStore config, Func<SiyuanConnectionConfig, ISiyuanClient> clientFactory)
    {
        app.MapGet("/api/projects", () => Results.Json(config.GetSnapshot().Projects));

        app.MapPost("/api/projects", (ProjectConfig body) =>
        {
            try { config.Update(c => c.Projects.Add(body)); return Results.Json(body); }
            catch (ConfigValidationException ex) { throw new ApiException(400, "VALIDATION", "项目校验失败", string.Join("; ", ex.Errors)); }
        });

        app.MapPut("/api/projects/{name}", (string name, ProjectConfig body) =>
        {
            name = Uri.UnescapeDataString(name);
            try
            {
                config.Update(c =>
                {
                    var i = c.Projects.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (i < 0) throw new ApiException(404, "NOT_FOUND", $"项目 '{name}' 不存在", null);
                    body.Name = name; // 保持标识一致
                    c.Projects[i] = body;
                });
                return Results.Json(body);
            }
            catch (ConfigValidationException ex) { throw new ApiException(400, "VALIDATION", "项目校验失败", string.Join("; ", ex.Errors)); }
        });

        app.MapDelete("/api/projects/{name}", (string name) =>
        {
            name = Uri.UnescapeDataString(name);
            bool removed = false;
            config.Update(c =>
            {
                var i = c.Projects.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (i < 0) throw new ApiException(404, "NOT_FOUND", $"项目 '{name}' 不存在", null);
                c.Projects.RemoveAt(i); removed = true;
            });
            return Results.Json(new { ok = removed });
        });

        // 批量/单个设置启用状态：一次事务只改 Enabled，避免前端拼完整对象整替（并发覆盖 + N 次写盘）。
        // 用 POST 字面段而非 PUT /api/projects/enabled，避免与 PUT /api/projects/{name} 参数段产生
        // "项目名恰好等于字面段时被截胡"的边界冲突。
        app.MapPost("/api/projects/set-enabled", (SetEnabledRequest body) =>
        {
            var names = (body.Names ?? [])
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count == 0) throw new ApiException(400, "VALIDATION", "names 不能为空", null);
            try
            {
                config.Update(c =>
                {
                    var missing = names.Where(n => !c.Projects.Any(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList();
                    if (missing.Count > 0) throw new ApiException(404, "NOT_FOUND", $"项目不存在：{string.Join(", ", missing)}", null);
                    foreach (var n in names)
                        c.Projects.First(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)).Enabled = body.Enabled;
                });
                return Results.Json(new { ok = true, count = names.Count });
            }
            catch (ConfigValidationException ex) { throw new ApiException(400, "VALIDATION", "项目校验失败", string.Join("; ", ex.Errors)); }
        });

        app.MapPost("/api/projects/{name}/init-parent", async (string name) =>
        {
            name = Uri.UnescapeDataString(name);
            var snap = config.GetSnapshot();
            var p = snap.Projects.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (p is null) throw new ApiException(404, "NOT_FOUND", $"项目 '{name}' 不存在", null);

            var siyuan = clientFactory(new(snap.Siyuan.ServerUrl, snap.Siyuan.Token));
            try
            {
                string parentPath;
                try { parentPath = PathNormalizer.NormalizeParentPath(p.ParentPath); }
                catch (PathNormalizerException e) { throw new ApiException(400, "VALIDATION", "parentPath 无效", e.Message); }

                var notebooks = await siyuan.ListNotebooksAsync(default);
                var nbName = string.IsNullOrWhiteSpace(p.Notebook) ? snap.Siyuan.DefaultNotebook : p.Notebook;
                var nb = notebooks.FirstOrDefault(n => n.Name == nbName) ?? throw new ApiException(400, "NOTEBOOK_MISSING", $"笔记本 '{nbName}' 不存在", null);

                // 已存在？
                var existing = await siyuan.GetDocIdsByHPathAsync(nb.Id, parentPath, default);
                if (existing.Count > 0) return Results.Json(new { ok = true, created = false, message = "思源中已存在" });

                // 逐级创建（处理是否自动建中间层级的不确定性）
                var segments = parentPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var path = "";
                string createdId = "";
                foreach (var seg in segments)
                {
                    path += "/" + seg;
                    var ids = await siyuan.GetDocIdsByHPathAsync(nb.Id, path, default);
                    if (ids.Count == 0)
                        createdId = await siyuan.CreateDocWithMdAsync(nb.Id, path, "", default);
                }
                return Results.Json(new { ok = true, created = true, docId = createdId });
            }
            catch (ApiException) { throw; }
            catch (SiyuanAuthException) { throw new ApiException(401, "AUTH", "token 或权限无效", null); }
            catch (Exception ex) { throw new ApiException(502, "UNREACHABLE", "思源不可达", ex.Message); }
        });
    }
}
