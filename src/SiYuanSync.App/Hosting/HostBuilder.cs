using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SiYuanSync.App.Worker;
using SiYuanSync.Core.Config;
using SiYuanSync.Core.Paths;
using SiYuanSync.Core.State;
using SiYuanSync.Core.Sync;

namespace SiYuanSync.App.Hosting;

public static class HostBuilder
{
    public static IHost Build(string[] args)
    {
        bool console = args.Contains("--console");

        var builder = Host.CreateApplicationBuilder(args);
        if (!console)
            builder.Services.AddWindowsService(o => o.ServiceName = "SiYuan-AI-Expand");

        builder.Services.AddLogging(l => l.AddSimpleConsole());

        // 数据目录与配置
        AppPaths.EnsureDataDir();
        var configStore = new ConfigStore(AppPaths.GetConfigPath());
        configStore.Initialize(); // 损坏会抛，由 Program 捕获
        builder.Services.AddSingleton(configStore);
        builder.Services.AddSingleton<IStateStore>(_ => new StateStore(AppPaths.GetStateDbPath()));
        builder.Services.AddSingleton<SyncEngine>();

        builder.Services.AddHostedService<TimedSyncService>();

        return builder.Build();
    }
}
