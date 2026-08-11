using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SiYuanSync.Core.Config;
using SiYuanSync.Core.Models;

namespace SiYuanSync.App.Mcp;

/// <summary>工具执行级异常：映射为 tools/call 的 isError=true 文本结果（而非 JSON-RPC error）。</summary>
internal sealed class McpToolException : Exception
{
    public McpToolException(string message) : base(message) { }
}

/// <summary>
/// MCP（Model Context Protocol）端点：Streamable HTTP 形态（POST /mcp，JSON-RPC 2.0）。
/// 协议版本 2025-06-18；仅无状态工具调用，不开 SSE 流。绕过 Web 管理台的会话/CSRF，
/// 由独立 Kestrel 监听 loopback，供本机 AI 客户端（Claude Desktop / Cursor 等）接入。
/// </summary>
public static class McpEndpoints
{
    private const string ProtocolVersion = "2025-06-18";
    private const string ServerName = "SiYuan-AI-Expand";
    private const string ContentType = "application/json; charset=utf-8";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 保留中文可读
    };

    public static void Map(IEndpointRouteBuilder app, ConfigStore config, string serverVersion)
        => app.MapPost("/mcp", (HttpContext ctx) => HandleAsync(ctx, config, serverVersion));

    private static async Task HandleAsync(HttpContext ctx, ConfigStore config, string serverVersion)
    {
        JsonDocument doc;
        try { doc = await JsonDocument.ParseAsync(ctx.Request.Body); }
        catch
        {
            ctx.Response.StatusCode = 400;
            await WriteAsync(ctx, Failure(null, -32700, "请求体非合法 JSON"));
            return;
        }

        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            // batch：逐条处理，过滤通知（无响应），其余回数组
            var responses = new List<object>();
            foreach (var item in root.EnumerateArray())
            {
                var r = HandleOne(item, config, serverVersion, ctx);
                if (r is not null) responses.Add(r);
            }
            if (responses.Count == 0) { ctx.Response.StatusCode = 202; return; }
            await WriteAsync(ctx, responses);
            return;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            ctx.Response.StatusCode = 400;
            await WriteAsync(ctx, Failure(null, -32600, "请求根必须为 JSON 对象或数组"));
            return;
        }

        var one = HandleOne(root, config, serverVersion, ctx);
        // 通知（无 id）：HTTP 202，无 body
        if (one is null) { ctx.Response.StatusCode = 202; return; }
        await WriteAsync(ctx, one);
    }

    /// <summary>处理单条 JSON-RPC：返回响应对象；通知（无 id）返回 null（不回 body）。</summary>
    private static object? HandleOne(JsonElement el, ConfigStore config, string serverVersion, HttpContext ctx)
    {
        var (id, hasId) = ReadId(el);
        if (!el.TryGetProperty("method", out var methodEl) || methodEl.ValueKind != JsonValueKind.String)
            return hasId ? Failure(id, -32600, "缺少 method") : null;

        var method = methodEl.GetString() ?? "";
        JsonElement? p = el.TryGetProperty("params", out var pe) && pe.ValueKind == JsonValueKind.Object ? pe : null;

        try
        {
            return method switch
            {
                "initialize" => Initialize(id, serverVersion, ctx),
                "notifications/initialized" => null,
                "tools/list" => Success(id, new { tools = new[] { AddProjectTool() } }),
                "tools/call" => ToolsCall(id, p, config),
                "ping" => Success(id, new { }),
                _ => hasId ? Failure(id, -32601, $"未知方法：{method}") : null
            };
        }
        catch (McpToolException ex) { return hasId ? ToolError(id, ex.Message) : null; }
        catch (ConfigValidationException ex) { return hasId ? ToolError(id, "校验失败：" + string.Join("; ", ex.Errors)) : null; }
        catch (Exception ex) { return hasId ? Failure(id, -32603, "内部错误：" + ex.Message) : null; }
    }

    private static object Initialize(object? id, string serverVersion, HttpContext ctx)
    {
        // Streamable HTTP：initialize 响应须回带 Mcp-Session-Id；无状态实现不后续校验
        ctx.Response.Headers["Mcp-Session-Id"] = "siyuan-mcp-" + Guid.NewGuid().ToString("N");
        return Success(id, new
        {
            protocolVersion = ProtocolVersion,
            capabilities = new { tools = new { } },
            serverInfo = new { name = ServerName, version = serverVersion }
        });
    }

    private static object ToolsCall(object? id, JsonElement? p, ConfigStore config)
    {
        if (p is null)
            return Failure(id, -32602, "tools/call 缺少 params");
        if (!p.Value.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            return Failure(id, -32602, "tools/call 缺少 params.name");
        var tool = nameEl.GetString() ?? "";
        if (tool != "add_project")
            return Failure(id, -32602, $"未知工具：{tool}（当前仅支持 add_project）");

        JsonElement? args = p.Value.TryGetProperty("arguments", out var ae) && ae.ValueKind == JsonValueKind.Object ? ae : null;
        var name = GetStr(args, "name");
        var docPath = GetStr(args, "docPath");
        if (string.IsNullOrWhiteSpace(name))
            return ToolError(id, "add_project 缺少必填参数 name");
        if (string.IsNullOrWhiteSpace(docPath))
            return ToolError(id, "add_project 缺少必填参数 docPath");

        var proj = new ProjectConfig
        {
            Name = name!.Trim(),
            DocPath = docPath!.Trim(),
            Enabled = true,
            Notebook = "",
            ParentPath = ""
        };
        config.Update(c =>
        {
            if (c.Projects.Any(x => x.Name.Equals(proj.Name, StringComparison.OrdinalIgnoreCase)))
                throw new McpToolException($"项目已存在：{proj.Name}");
            c.Projects.Add(proj);
        });

        return Success(id, new
        {
            content = new[]
            {
                new { type = "text", text = $"已新增项目 '{proj.Name}'（docPath={proj.DocPath}）。可前往 Web 控制台补充 notebook/parentPath 后启动同步。" }
            },
            isError = false
        });
    }

    private static object AddProjectTool() => new
    {
        name = "add_project",
        description = "新增思源同步项目（仅新增，不能修改/删除）。传入项目名和本地文档目录路径；项目名重复或 docPath 与既有项目重叠将失败。",
        inputSchema = new
        {
            type = "object",
            properties = new
            {
                name = new { type = "string", description = "项目名（唯一标识，不区分大小写）" },
                docPath = new { type = "string", description = "本地文档目录路径（绝对或相对）" }
            },
            required = new[] { "name", "docPath" }
        }
    };

    // ===== JSON-RPC 辅助 =====

    private static (object? id, bool hasId) ReadId(JsonElement el)
    {
        if (!el.TryGetProperty("id", out var idEl)) return (null, false);
        return idEl.ValueKind switch
        {
            JsonValueKind.String => (idEl.GetString(), true),
            JsonValueKind.Number => ((object?)idEl.GetInt64(), true),
            JsonValueKind.Null => (null, true),
            _ => (null, true)
        };
    }

    private static string GetStr(JsonElement? obj, string prop) =>
        obj.HasValue && obj.Value.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    /// <summary>JSON-RPC 成功响应：{jsonrpc, id, result}。</summary>
    private static object Success(object? id, object result) => new { jsonrpc = "2.0", id, result };

    /// <summary>JSON-RPC 错误响应：{jsonrpc, id, error:{code,message}}。</summary>
    private static object Failure(object? id, int code, string message) =>
        new { jsonrpc = "2.0", id, error = new { code, message } };

    /// <summary>tools/call 工具级失败：成功壳 + isError=true 文本（MCP 规范）。</summary>
    private static object ToolError(object? id, string message) =>
        new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                content = new[] { new { type = "text", text = message } },
                isError = true
            }
        };

    private static async Task WriteAsync(HttpContext ctx, object payload)
    {
        ctx.Response.ContentType = ContentType;
        await JsonSerializer.SerializeAsync(ctx.Response.Body, payload, JsonOpts);
    }
}
