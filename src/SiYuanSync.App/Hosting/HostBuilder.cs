using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using SiYuanSync.App.Autostart;
using SiYuanSync.App.Web;
using SiYuanSync.App.Worker;
using SiYuanSync.Core.Config;
using SiYuanSync.Core.Models;
using SiYuanSync.Core.Paths;
using SiYuanSync.Core.Siyuan;
using SiYuanSync.Core.State;
using SiYuanSync.Core.Sync;

namespace SiYuanSync.App.Hosting;

public static class HostBuilder
{
    public static IHost Build(string[] args)
    {
        bool console = args.Contains("--console");

        // 用 Host.CreateDefaultBuilder（IHostBuilder）而非 CreateApplicationBuilder，
        // 因为 .NET 10 下 ConfigureWebHost 扩展仅存在于 IHostBuilder，
        // 且 IWebHost 已过时（ASPDEPR008）：统一用 IHost 同时承载 worker 与 Kestrel。
        // 仅托盘 / 控制台两种模式；不再作为 Windows 服务运行（UseWindowsService 已移除）。
        var hostBuilder = Host.CreateDefaultBuilder(args);

        hostBuilder.UseContentRoot(AppContext.BaseDirectory);

        // 数据目录与配置：在 Build 前即初始化（损坏会抛，由 Program 捕获）
        AppPaths.EnsureDataDir();
        var configStore = new ConfigStore(AppPaths.GetConfigPath());
        configStore.Initialize();

        // Serilog：File（按日 + 10MB rolling，retained 15）+ Console（仅 --console）
        //                + Windows EventLog（仅 Error 级）。
        // Destructurer 保证 SiyuanConfig 写入结构化日志时只暴露 serverUrl 与 hasToken，
        // 绝不把明文 Token 写入任何 sink。
        var logPath = Path.Combine(AppPaths.GetLogsDir(), "app-.log");
        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Destructure.ByTransforming<SiyuanConfig>(s => new { serverUrl = s.ServerUrl, hasToken = s.HasToken })
            .Enrich.FromLogContext()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                retainedFileCountLimit: 15,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {Message:lj}{NewLine}{Exception}");
        if (console) loggerConfig.WriteTo.Console();
        // EventLog sink 仅 Windows 可用；OperatingSystem.IsWindows() 同时抑制 CA1416。
        if (OperatingSystem.IsWindows())
            loggerConfig.WriteTo.EventLog("SiYuan-AI-Expand", restrictedToMinimumLevel: LogEventLevel.Error);
        Log.Logger = loggerConfig.CreateLogger();

        // 用 Serilog 替换默认日志管道（同时移除 Task 18 的 AddSimpleConsole，避免双重 Console）
        hostBuilder.ConfigureLogging(l =>
        {
            l.ClearProviders();
            l.AddSerilog(Log.Logger, dispose: true);
        });

        // clientFactory：每次调用产生独立的 ISiyuanClient。HttpClientHandler 不可共享
        // （随 HttpClient dispose），故工厂内每次 new。
        Func<SiyuanConnectionConfig, ISiyuanClient> clientFactory =
            conn => new RetryingSiyuanClient(new SiyuanClient(new HttpClientHandler(), conn));

        // 开机自启：主 exe 路径取自当前进程；Web 与托盘共用同一单例，统一读写 HKCU Run 键
        var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AppConstants.MainExeName);
        var autostart = new AutostartService(exePath);

        hostBuilder.ConfigureServices(services =>
        {
            services.AddSingleton(configStore);
            services.AddSingleton<IStateStore>(_ => new StateStore(AppPaths.GetStateDbPath()));
            services.AddSingleton(clientFactory);     // SyncEngine 依赖注入
            services.AddSingleton<SyncEngine>();
            services.AddSingleton<SiYuanSync.App.Siyuan.SiyuanAutoStartService>(); // 同步前自动拉起思源
            services.AddSingleton<RunCoordinator>();   // 立即同步的并发守卫
            services.AddSingleton<SessionStore>();     // Web 会话：WebHostBuilder 与 ConfigEndpoints 共享同一实例（密码热更时 RevokeAll）
            services.AddSingleton(autostart);          // 开机自启：SystemEndpoints 解析使用
            services.AddHostedService<TimedSyncService>();
        });

        // Kestrel 子主机：与通用宿主同进程，bind/port 取自 configStore 快照。
        // ConfigureWebHostDefaults 会注册 Kestrel（IServer）及默认服务，
        // 再在回调里追加我们的监听/静态资源/路由配置。
        hostBuilder.ConfigureWebHostDefaults(web => WebHostBuilder.ConfigureWebHost(web, configStore));

        return hostBuilder.Build();
    }
}
