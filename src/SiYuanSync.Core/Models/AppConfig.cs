namespace SiYuanSync.Core.Models;

public sealed class AppConfig
{
    public SiyuanConfig Siyuan { get; set; } = new();
    public SyncConfig Sync { get; set; } = new();
    public WebConfig Web { get; set; } = new();
    public List<ProjectConfig> Projects { get; set; } = new();
}
