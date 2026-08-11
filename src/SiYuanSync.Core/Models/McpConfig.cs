namespace SiYuanSync.Core.Models;

/// <summary>
/// MCP（Model Context Protocol）服务端配置：暴露 add_project 等工具供 AI 客户端调用。
/// 仅监听 loopback，避免外部网络暴露；端口可与 Web 管理端口不同。
/// </summary>
public sealed class McpConfig
{
    /// <summary>MCP 是否启用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>MCP 监听端口；与 Web 管理端口（默认 61122）区分。</summary>
    public int Port { get; set; } = 61123;

    /// <summary>监听地址；当前固定 loopback，不随配置变更。</summary>
    public string Bind { get; set; } = "127.0.0.1";
}
