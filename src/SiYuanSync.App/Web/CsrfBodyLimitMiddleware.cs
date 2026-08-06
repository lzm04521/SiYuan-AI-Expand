using System.Net;
using Microsoft.AspNetCore.Http;
using SiYuanSync.App.Web.Errors;

namespace SiYuanSync.App.Web;

public sealed class CsrfBodyLimitMiddleware
{
    private const long MaxBodyBytes = 1 * 1024 * 1024;
    private readonly RequestDelegate _next;
    public CsrfBodyLimitMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx)
    {
        var method = ctx.Request.Method;
        if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsDelete(method))
        {
            // 请求体大小
            if (ctx.Request.ContentLength is long len && len > MaxBodyBytes)
            { await ApiError.Write(ctx, 413, "PAYLOAD_TOO_LARGE", "请求体过大", null); return; }

            // CSRF：Origin/Referer 与 Host 一致。loopback 来源豁免（本机调试）。
            var remote = ctx.Connection.RemoteIpAddress;
            if (!WebAuthMiddleware.IsLoopback(remote))
            {
                var host = ctx.Request.Host.Value ?? "";
                bool ok = CheckHeader(ctx.Request.Headers.Origin, host) || CheckHeader(ctx.Request.Headers.Referer, host);
                if (!ok)
                { await ApiError.Write(ctx, 403, "CSRF_CHECK_FAILED", "来源校验失败", null); return; }
            }
        }
        await _next(ctx);
    }

    private static bool CheckHeader(string? header, string host)
    {
        if (string.IsNullOrEmpty(header)) return false;
        // Origin 是 scheme://host[:port]，Referer 是完整 URL
        try
        {
            var u = new Uri(header);
            return (u.Host + (u.IsDefaultPort ? "" : ":" + u.Port)) == host
                || u.Host + ":" + u.Port == host;
        }
        catch { return false; }
    }
}
