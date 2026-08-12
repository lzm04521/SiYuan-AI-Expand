namespace SiYuanSync.Core.Models;

/// <summary>
/// MCP（Model Context Protocol）服务端配置：暴露 add_project 等工具供 AI 客户端调用。
/// 与 Web 管理台共用同一 Kestrel 端口（POST /mcp 端点），不再独立监听端口；
/// 仅 loopback 来源可调用（WebAuthMiddleware 本机免认证 + 端点内 loopback 硬校验）。
/// </summary>
public sealed class McpConfig
{
    /// <summary>MCP 是否启用。</summary>
    public bool Enabled { get; set; } = true;
}
