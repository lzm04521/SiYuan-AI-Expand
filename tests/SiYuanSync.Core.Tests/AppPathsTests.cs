using SiYuanSync.Core.Models;
using SiYuanSync.Core.Paths;

using Xunit;

namespace SiYuanSync.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void DataDir_is_under_ProgramData()
    {
        var dir = AppPaths.GetDataDir();
        Assert.EndsWith(Path.Combine("SiYuan-AI-Expand"), dir);
        Assert.Contains("ProgramData", dir, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Derived_paths_live_under_data_dir()
    {
        Assert.Equal(Path.Combine(AppPaths.GetDataDir(), "config.json"), AppPaths.GetConfigPath());
        Assert.Equal(Path.Combine(AppPaths.GetDataDir(), "state.db"), AppPaths.GetStateDbPath());
        Assert.Equal(Path.Combine(AppPaths.GetDataDir(), "logs"), AppPaths.GetLogsDir());
    }

    [Fact]
    public void EnsureDataDir_created_idempotent()
    {
        AppPaths.EnsureDataDir();
        AppPaths.EnsureDataDir(); // 二次不抛
        Assert.True(Directory.Exists(AppPaths.GetDataDir()));
    }

    [Fact]
    public void Default_AppConfig_has_safe_values()
    {
        var cfg = new AppConfig();
        Assert.Equal("http://127.0.0.1:6806", cfg.Siyuan.ServerUrl);
        Assert.Equal("", cfg.Siyuan.Token);
        Assert.Equal(10, cfg.Sync.IntervalMinutes);
        Assert.True(cfg.Sync.RunOnStart);
        Assert.Equal(6807, cfg.Web.Port);
        Assert.Equal("127.0.0.1", cfg.Web.Bind);
        Assert.Equal("", cfg.Web.Password);
        Assert.Empty(cfg.Projects);
    }
}
