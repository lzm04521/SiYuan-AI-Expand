using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SiYuanSync.App.Web.Errors;
using SiYuanSync.Core.Config;
using SiYuanSync.Core.Models;

namespace SiYuanSync.App.Web.Endpoints;

public static class ConfigEndpoints
{
    public static void Map(IEndpointRouteBuilder app, ConfigStore config)
    {
        app.MapGet("/api/config", () => Results.Json(config.GetSnapshotForDisplay()));

        app.MapPut("/api/config", (SiyuanConfigDto body) =>
        {
            try
            {
                config.Update(c =>
                {
                    c.Siyuan.ServerUrl = body.ServerUrl ?? c.Siyuan.ServerUrl;
                    c.Siyuan.Token = TokenMasking.PreserveOriginalIfMasked(body.Token ?? "", c.Siyuan.Token);
                    c.Siyuan.DefaultNotebook = body.DefaultNotebook ?? c.Siyuan.DefaultNotebook;
                    c.Sync.IntervalMinutes = body.IntervalMinutes ?? c.Sync.IntervalMinutes;
                    c.Sync.RunOnStart = body.RunOnStart ?? c.Sync.RunOnStart;
                });
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
    }
}
