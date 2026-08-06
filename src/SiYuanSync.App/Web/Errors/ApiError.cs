using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SiYuanSync.App.Web.Errors;

public sealed class ApiException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public string? Details { get; }
    public ApiException(int status, string code, string message, string? details = null) : base(message)
    { Status = status; Code = code; Details = details; }
}

public static class ApiError
{
    public static async Task Write(HttpContext ctx, int status, string code, string message, string? details)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var payload = JsonSerializer.Serialize(new { code, message, details });
        await ctx.Response.WriteAsync(payload);
    }
}
