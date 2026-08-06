using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SiYuanSync.App.Worker;

public sealed class TimedSyncService : BackgroundService
{
    private readonly ILogger<TimedSyncService> _logger;
    public TimedSyncService(ILogger<TimedSyncService> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stopToken)
    {
        _logger.LogInformation("TimedSyncService 启动（占位）");
        await Task.Delay(Timeout.Infinite, stopToken);
    }
}
