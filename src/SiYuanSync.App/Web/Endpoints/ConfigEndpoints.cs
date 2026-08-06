using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SiYuanSync.App.Web.Errors;
using SiYuanSync.Core.Config;
using SiYuanSync.Core.Models;

namespace SiYuanSync.App.Web.Endpoints;

public static class ConfigEndpoints
{
    public static void Map(IEndpointRouteBuilder app, ConfigStore config, SessionStore sessions)
    {
        app.MapGet("/api/config", () => Results.Json(config.GetSnapshotForDisplay()));

        app.MapPut("/api/config", (SiyuanConfigDto body) =>
        {
            try
            {
                // 记录变更前 web.password，更新后若不同则吊销所有 session
                // （旧密码登录的 cookie 立即失效；SessionStore 内存态，重启亦自然失效）
                var before = config.GetSnapshot().Web.Password;
                config.Update(c =>
                {
                    c.Siyuan.ServerUrl = body.ServerUrl ?? c.Siyuan.ServerUrl;
                    c.Siyuan.Token = TokenMasking.PreserveOriginalIfMasked(body.Token ?? "", c.Siyuan.Token);
                    c.Siyuan.DefaultNotebook = body.DefaultNotebook ?? c.Siyuan.DefaultNotebook;
                    c.Sync.IntervalMinutes = body.IntervalMinutes ?? c.Sync.IntervalMinutes;
                    c.Sync.RunOnStart = body.RunOnStart ?? c.Sync.RunOnStart;
                    c.Web.Password = body.WebPassword ?? c.Web.Password;
                });
                var after = config.GetSnapshot().Web.Password;
                if (!WebAuthMiddleware.FixedTimeEquals(before, after))
                    sessions.RevokeAll();
                return Results.Json(config.GetSnapshotForDisplay());
            }
            catch (ConfigValidationException ex)
            { throw new ApiException(400, "VALIDATION", "配置校验失败", string.Join("; ", ex.Errors)); }
        });
    }

    public sealed class SiyuanConfigDto
    {
        public string? ServerUrl { get; set; }
        public string? Token { get; set; }
        public string? DefaultNotebook { get; set; }
        public int? IntervalMinutes { get; set; }
        public bool? RunOnStart { get; set; }
        public string? WebPassword { get; set; }
    }
}
