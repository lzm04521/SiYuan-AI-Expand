using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SiYuanSync.Core.Config;

namespace SiYuanSync.App.Mcp;

/// <summary>
/// 以独立 IHostedService 承载第二个 Kestrel：监听 MCP 端口（loopback），注册 MCP 端点。
/// 与 Web 管理台 pipeline 完全隔离——无会话/CSRF 中间件；启动失败仅记日志，不阻断主程序。
/// 端口在服务启动时读取快照，变更需重启进程才生效。
/// </summary>
public sealed class McpServerHostedService : IHostedService
{
    private readonly ConfigStore _config;
    private readonly ILogger<McpServerHostedService> _logger;
    private Microsoft.Extensions.Hosting.IHost? _inner;

    public McpServerHostedService(ConfigStore config, ILogger<McpServerHostedService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var snap = _config.GetSnapshot();
        if (!snap.Mcp.Enabled)
        {
            _logger.LogInformation("MCP 未启用（mcp.enabled=false），跳过启动");
            return;
        }

        var port = snap.Mcp.Port;
        var bindStr = string.IsNullOrWhiteSpace(snap.Mcp.Bind) ? "127.0.0.1" : snap.Mcp.Bind;
        var bind = MapBind(bindStr);
        var serverVersion = AppVersion.CurrentString;

        try
        {
            // 内嵌迷你 WebHost：复用主 Serilog 日志；不读 appsettings（避免与主配置混淆）
            _inner = new HostBuilder()
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureLogging(l =>
                {
                    l.ClearProviders();
                    l.AddSerilog(Log.Logger, dispose: false);
                })
                .ConfigureWebHostDefaults(web =>
                {
                    web.ConfigureKestrel(k => k.Listen(bind, port));
                    web.ConfigureServices(s => s.AddRouting());
                    web.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(ep => McpEndpoints.Map(ep, _config, serverVersion));
                    });
                })
                .Build();

            await _inner.StartAsync(cancellationToken);
            _logger.LogInformation("MCP 已启动，监听 {Bind}:{Port}（POST /mcp）", bindStr, port);
        }
        catch (Exception ex)
        {
            // 端口被占等启动失败：仅记错误，不影响 Web 同步主功能
            _logger.LogError(ex, "MCP 启动失败（{Bind}:{Port}）；Web 同步功能不受影响", bindStr, port);
            _inner = null;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_inner is null) return;
        try { await _inner.StopAsync(cancellationToken); }
        catch (Exception ex) { _logger.LogWarning(ex, "MCP 停止时出错"); }
    }

    private static IPAddress MapBind(string bind) => bind switch
    {
        "127.0.0.1" or "localhost" => IPAddress.Loopback,
        "::1" => IPAddress.IPv6Loopback,
        "0.0.0.0" => IPAddress.Any,
        _ => IPAddress.Loopback
    };
}
