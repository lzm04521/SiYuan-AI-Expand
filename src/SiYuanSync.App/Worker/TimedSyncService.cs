using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SiYuanSync.App.Web;
using SiYuanSync.Core.Config;

namespace SiYuanSync.App.Worker;

public sealed class TimedSyncService : BackgroundService
{
    private readonly RunCoordinator _runner;
    private readonly ConfigStore _config;
    private readonly ILogger<TimedSyncService> _logger;
    public TimedSyncService(RunCoordinator runner, ConfigStore config, ILogger<TimedSyncService> logger)
    { _runner = runner; _config = config; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stopToken)
    {
        var snap = _config.GetSnapshot();
        if (snap.Sync.RunOnStart)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(15), stopToken); }
            catch (OperationCanceledException) { return; }
            await TriggerOnceAsync(stopToken);
        }

        while (!stopToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(1, _config.GetSnapshot().Sync.IntervalMinutes));
            try { await Task.Delay(interval, stopToken); }
            catch (OperationCanceledException) { break; }
            await TriggerOnceAsync(stopToken);
        }
    }

    private async Task TriggerOnceAsync(CancellationToken ct)
    {
        if (_runner.IsRunning) { _logger.LogInformation("上一轮仍在进行，跳过本次触发"); return; }
        try { await _runner.TryStartAsync(ct); }
        catch (Exception ex) { _logger.LogError(ex, "触发同步失败"); }
    }
}
