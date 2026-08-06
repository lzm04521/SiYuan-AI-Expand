namespace SiYuanSync.Core.Models;

public sealed class SyncConfig
{
    public int IntervalMinutes { get; set; } = 10;
    public bool RunOnStart { get; set; } = true;
}
