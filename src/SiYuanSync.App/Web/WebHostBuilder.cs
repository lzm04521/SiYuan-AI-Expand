using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using SiYuanSync.App.Web.Errors;
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
            // 最外层：全局异常兜底（ApiException 走统一格式，其余 500 通用错误不泄露堆栈）
            app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
            {
                var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
                if (ex is ApiException ae)
                    await ApiError.Write(ctx, ae.Status, ae.Code, ae.Message, ae.Details);
                else
                    await ApiError.Write(ctx, 500, "INTERNAL", "内部错误", null);
            }));

            var asm = typeof(WebHostBuilder).Assembly;
            var fp = new ManifestEmbeddedFileProvider(asm, "Web/wwwroot");
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fp, RequestPath = "" });

            var sessions = new SessionStore();
            var rate = new LoginRateLimiter();
            // 顺序：CSRF/请求体限制 → 认证 → 路由
            app.UseMiddleware<CsrfBodyLimitMiddleware>();
            app.UseMiddleware<WebAuthMiddleware>(sessions, rate, new Func<string>(() => config.GetSnapshot().Web.Password));

            // POST /api/login：限流 → 解析 password → 恒定时间比较 → 签发 session cookie。
            // 用 app.Map 分支与 /health 风格一致（项目未启用 endpoint routing）。
            app.Map("/api/login", login => login.Run(async ctx =>
            {
                if (!HttpMethods.IsPost(ctx.Request.Method))
                { ctx.Response.StatusCode = 405; return; }
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
                if (!rate.TryConsume(ip)) { ctx.Response.StatusCode = 429; await ctx.Response.WriteAsync("""{"code":429,"message":"请求过于频繁","details":null}"""); return; }
                using var sr = new StreamReader(ctx.Request.Body); var body = await sr.ReadToEndAsync();
                string? pwd = null;
                try { using var doc = System.Text.Json.JsonDocument.Parse(body); pwd = doc.RootElement.GetProperty("password").GetString(); } catch { }
                var actual = config.GetSnapshot().Web.Password;
                if (!WebAuthMiddleware.FixedTimeEquals(pwd ?? "", actual))
                { ctx.Response.StatusCode = 401; await ctx.Response.WriteAsync("""{"code":401,"message":"用户名或密码错误","details":null}"""); return; }
                var sid = sessions.Issue();
                ctx.Response.Cookies.Append(WebAuthMiddleware.SessionCookie, sid, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Expires = DateTimeOffset.UtcNow.AddHours(8) });
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"ok":true}""");
            }));

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
