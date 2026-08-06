using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using SiYuanSync.Core.Config;

namespace SiYuanSync.App.Web;

/// <summary>
/// 配置嵌入的 Kestrel 子主机：静态资源以 ManifestEmbeddedFileProvider 暴露，
/// /health 返回 "ok"，/ 与 /index.html 返回内嵌 index.html，其它路径 404。
/// 在 .NET 10 下 IWebHost 已过时，使用 IWebHostBuilder + 主宿主 ConfigureWebHost 扩展组合。
/// </summary>
public static class WebHostBuilder
{
    /// <summary>
    /// 在给定的 IWebHostBuilder 上应用 Kestrel 监听、静态文件、健康检查、SPA 入口配置。
    /// bind/port 取自配置快照。
    /// </summary>
    public static void ConfigureWebHost(IWebHostBuilder web, ConfigStore config)
    {
        var snap = config.GetSnapshot();
        var listenAddr = MapBind(snap.Web.Bind);
        var port = snap.Web.Port;

        web.ConfigureKestrel(k => k.Listen(listenAddr, port));
        web.ConfigureServices(_ => { });
        web.Configure(app =>
        {
            var asm = typeof(WebHostBuilder).Assembly;
            var fp = new ManifestEmbeddedFileProvider(asm, "Web/wwwroot");
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fp, RequestPath = "" });

            app.Map("/health", health => health.Run(async ctx =>
            {
                ctx.Response.ContentType = "text/plain";
                await ctx.Response.WriteAsync("ok");
            }));

            app.Run(async ctx =>
            {
                if (ctx.Request.Path == "/" || ctx.Request.Path == "/index.html")
                {
                    var fi = fp.GetFileInfo("index.html");
                    if (fi.Exists)
                    {
                        await using var rs = fi.CreateReadStream();
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        await rs.CopyToAsync(ctx.Response.Body);
                        return;
                    }
                }
                ctx.Response.StatusCode = 404;
            });
        });
    }

    private static IPAddress MapBind(string bind) => bind switch
    {
        "127.0.0.1" => IPAddress.Loopback,
        "localhost" => IPAddress.Loopback,
        "::1" => IPAddress.IPv6Loopback,
        "0.0.0.0" => IPAddress.Any,
        _ => IPAddress.Loopback
    };
}
