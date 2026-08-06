using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        var hostBuilder = Host.CreateDefaultBuilder(args);
        if (!console)
            hostBuilder.UseWindowsService(o => o.ServiceName = "SiYuan-AI-Expand");

        hostBuilder.ConfigureLogging(l => l.AddSimpleConsole());
        hostBuilder.UseContentRoot(AppContext.BaseDirectory);

        // 数据目录与配置：在 Build 前即初始化（损坏会抛，由 Program 捕获）
        AppPaths.EnsureDataDir();
        var configStore = new ConfigStore(AppPaths.GetConfigPath());
        configStore.Initialize();

        // clientFactory：每次调用产生独立的 ISiyuanClient。HttpClientHandler 不可共享
        // （随 HttpClient dispose），故工厂内每次 new。
        Func<SiyuanConnectionConfig, ISiyuanClient> clientFactory =
            conn => new RetryingSiyuanClient(new SiyuanClient(new HttpClientHandler(), conn));

        hostBuilder.ConfigureServices(services =>
        {
            services.AddSingleton(configStore);
            services.AddSingleton<IStateStore>(_ => new StateStore(AppPaths.GetStateDbPath()));
            services.AddSingleton(clientFactory);     // SyncEngine 依赖注入
            services.AddSingleton<SyncEngine>();
            services.AddSingleton<RunCoordinator>();   // 立即同步的并发守卫
            services.AddHostedService<TimedSyncService>();
        });

        // Kestrel 子主机：与通用宿主同进程，bind/port 取自 configStore 快照。
        // ConfigureWebHostDefaults 会注册 Kestrel（IServer）及默认服务，
        // 再在回调里追加我们的监听/静态资源/路由配置。
        hostBuilder.ConfigureWebHostDefaults(web => WebHostBuilder.ConfigureWebHost(web, configStore));

        return hostBuilder.Build();
    }
}
