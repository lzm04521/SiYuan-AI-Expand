using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using SiYuanSync.Core.Config;

namespace SiYuanSync.App.Web;

public sealed class WebAuthMiddleware
{
    public const string SessionCookie = "sye_session";
    private readonly RequestDelegate _next;
    private readonly SessionStore _sessions;
    private readonly LoginRateLimiter _rate;
    private readonly Func<string> _passwordProvider;   // 动态读当前密码（密码热更后即时生效）

    public WebAuthMiddleware(RequestDelegate next, SessionStore sessions, LoginRateLimiter rate, Func<string> passwordProvider)
    { _next = next; _sessions = sessions; _rate = rate; _passwordProvider = passwordProvider; }

    public async Task Invoke(HttpContext ctx, ConfigStore config)
    {
        var path = ctx.Request.Path.Value ?? "";
        // 静态资源与公开端点放行
        if (IsStatic(path) || path == "/health" || path == "/api/login")
        { await _next(ctx); return; }

        if (IsLoopback(ctx.Connection.RemoteIpAddress))
        {
            // 本机免认证模式：仍服务端拒绝非 loopback 来源（由 RemoteIp 判断）
            await _next(ctx);
            return;
        }

        // 非 loopback：必须有 session
        if (ctx.Request.Cookies.TryGetValue(SessionCookie, out var sid) && _sessions.IsValid(sid))
        { await _next(ctx); return; }

        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsync("""{"code":401,"message":"未认证","details":null}""");
    }

    public static bool IsLoopback(System.Net.IPAddress? ip) =>
        ip is not null && (System.Net.IPAddress.IsLoopback(ip));

    private static bool IsStatic(string path) =>
        path.StartsWith("/styles.css", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/app.js", StringComparison.OrdinalIgnoreCase) ||
        path == "/" || path == "/index.html";

    public static bool FixedTimeEquals(string a, string b)
    {
        var ba = System.Text.Encoding.UTF8.GetBytes(a ?? "");
        var bb = System.Text.Encoding.UTF8.GetBytes(b ?? "");
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
