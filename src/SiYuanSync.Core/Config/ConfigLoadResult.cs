using SiYuanSync.Core.Models;

namespace SiYuanSync.Core.Config;

public abstract record ConfigLoadResult
{
    public sealed record Loaded(AppConfig Config) : ConfigLoadResult;
    public sealed record Missing : ConfigLoadResult;
    public sealed record Corrupt(string Reason) : ConfigLoadResult;
}
