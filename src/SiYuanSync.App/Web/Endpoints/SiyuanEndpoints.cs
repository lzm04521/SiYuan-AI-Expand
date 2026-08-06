using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SiYuanSync.App.Web.Errors;
using SiYuanSync.Core.Config;
using SiYuanSync.Core.Models;
using SiYuanSync.Core.Siyuan;

namespace SiYuanSync.App.Web.Endpoints;

public static class SiyuanEndpoints
{
    public static void Map(IEndpointRouteBuilder app, ConfigStore config, Func<SiyuanConnectionConfig, ISiyuanClient> clientFactory)
    {
        app.MapPost("/api/siyuan/test", async () =>
        {
            try
            {
                var snap = config.GetSnapshot();
                var siyuan = clientFactory(new(snap.Siyuan.ServerUrl, snap.Siyuan.Token));
                var nbs = await siyuan.ListNotebooksAsync(default);
                return Results.Json(new { ok = true, notebooks = nbs.Select(n => n.Name).ToArray() });
            }
            catch (SiyuanAuthException) { throw new ApiException(401, "AUTH", "token 或权限无效", null); }
            catch (Exception ex) { throw new ApiException(502, "UNREACHABLE", "思源不可达", ex.Message); }
        });
    }
}
