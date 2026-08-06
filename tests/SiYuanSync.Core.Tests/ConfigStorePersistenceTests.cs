using SiYuanSync.Core.Config;
using SiYuanSync.Core.Models;

using Xunit;

namespace SiYuanSync.Core.Tests;

public class ConfigStorePersistenceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;
    public ConfigStorePersistenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sye-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "config.json");
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Missing_file_creates_safe_default_and_returns_it()
    {
        var store = new ConfigStore(_path);
        var cfg = store.LoadOrInit();
        Assert.Equal("127.0.0.1", cfg.Web.Bind);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void Save_is_atomic_no_tmp_remains()
    {
        var store = new ConfigStore(_path);
        store.LoadOrInit();
        var cfg = new AppConfig { Web = { Port = 8000 }, Sync = { IntervalMinutes = 5 } };
        store.Save(cfg);

        Assert.False(File.Exists(_path + ".tmp"));
        var loaded = new ConfigStore(_path).LoadOrInit();
        Assert.Equal(8000, loaded.Web.Port);
        Assert.Equal(5, loaded.Sync.IntervalMinutes);
    }

    [Fact]
    public void Corrupt_file_throws_and_is_preserved()
    {
        File.WriteAllText(_path, "{ broken", System.Text.Encoding.UTF8);
        var store = new ConfigStore(_path);
        Assert.Throws<ConfigCorruptException>(() => store.LoadOrInit());
        Assert.True(File.Exists(_path)); // 原文件保留未被覆盖
    }

    [Fact]
    public void Invalid_config_save_throws_and_file_unchanged()
    {
        var store = new ConfigStore(_path);
        store.LoadOrInit();
        var before = File.ReadAllText(_path, System.Text.Encoding.UTF8);
        var bad = new AppConfig { Web = { Port = 99999 } };

        var ex = Assert.Throws<ConfigValidationException>(() => store.Save(bad));
        Assert.Contains(ex.Errors, e => e.Contains("port", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, File.ReadAllText(_path, System.Text.Encoding.UTF8));
    }

    [Fact]
    public void Leftover_tmp_is_promoted_on_next_load()
    {
        // 模拟上次替换中断：留下完整 .tmp，主文件缺失或更旧
        var good = new AppConfig { Web = { Port = 9000 }, Sync = { IntervalMinutes = 3 } };
        File.WriteAllText(_path + ".tmp", ConfigSerializer.Serialize(good), System.Text.Encoding.UTF8);

        var loaded = new ConfigStore(_path).LoadOrInit();
        Assert.Equal(9000, loaded.Web.Port);
        Assert.False(File.Exists(_path + ".tmp"));
    }
}
