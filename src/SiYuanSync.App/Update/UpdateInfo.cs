namespace SiYuanSync.App.Update;

/// <summary>一次升级检查的结果。</summary>
public sealed class UpdateCheckResult
{
    public bool HasUpdate { get; set; }
    public UpdateInfo? Update { get; set; }
    public string? Error { get; set; }
}

/// <summary>远端 Release 中匹配到的可升级资产信息。</summary>
public sealed class UpdateInfo
{
    public Version Version { get; set; } = new();
    public string ReleaseUrl { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string AssetName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public long SizeBytes { get; set; }
}
