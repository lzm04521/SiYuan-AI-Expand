using SiYuanSync.Core.Config;
using SiYuanSync.Core.Models;

using Xunit;

namespace SiYuanSync.Core.Tests;

public class ConfigStoreConcurrencyTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;
    public ConfigStoreConcurrencyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sye-cfgc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "config.json");
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Initialize_loads_or_creates_default()
    {
        var store = new ConfigStore(_path);
        store.Initialize();
        Assert.Equal("127.0.0.1", store.GetSnapshot().Web.Bind);
    }

    [Fact]
    public void Snapshot_is_independent_copy()
    {
        var store = new ConfigStore(_path);
        store.Initialize();
        var snap = store.GetSnapshot();
        snap.Web.Port = 12345;
        Assert.NotEqual(12345, store.GetSnapshot().Web.Port);
    }

    [Fact]
    public void Update_mutates_memory_and_persists()
    {
        var store = new ConfigStore(_path);
        store.Initialize();
        store.Update(c => c.Sync.IntervalMinutes = 42);

        Assert.Equal(42, store.GetSnapshot().Sync.IntervalMinutes);
        Assert.Equal(42, new ConfigStore(_path).LoadOrInit().Sync.IntervalMinutes);
    }

    [Fact]
    public void Update_is_atomic_validation_failure_keeps_old()
    {
        var store = new ConfigStore(_path);
        store.Initialize();
        store.Update(c => c.Sync.IntervalMinutes = 5);

        Assert.Throws<ConfigValidationException>(() =>
            store.Update(c => c.Web.Port = 99999));

        // 5 仍在内存与磁盘
        Assert.Equal(5, store.GetSnapshot().Sync.IntervalMinutes);
        Assert.Equal(5, new ConfigStore(_path).LoadOrInit().Sync.IntervalMinutes);
    }

    [Fact]
    public void Concurrent_updates_do_not_lose_fields()
    {
        var store = new ConfigStore(_path);
        store.Initialize();

        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
            store.Update(c =>
            {
                c.Projects.Add(new ProjectConfig
                {
                    Name = $"P{i}",
                    DocPath = Path.Combine(_dir, $"p{i}"),
                    Notebook = "AI",
                    ParentPath = $"/P{i}"
                });
            }))).ToArray();
#pragma warning disable xUnit1031
        Task.WaitAll(tasks);
#pragma warning restore xUnit1031

        Assert.Equal(50, store.GetSnapshot().Projects.Count);
        Assert.Equal(50, store.GetSnapshot().Projects.Select(p => p.Name).Distinct().Count());
    }

    [Fact]
    public void Display_snapshot_masks_token()
    {
        var store = new ConfigStore(_path);
        store.Initialize();
        store.Update(c => c.Siyuan.Token = "real-secret-token");

        var display = store.GetSnapshotForDisplay();
        Assert.Equal(TokenMasking.MaskedPlaceholder, display.Siyuan.Token);
        Assert.True(display.Siyuan.HasToken);
        // 运行快照仍是明文
        Assert.Equal("real-secret-token", store.GetSnapshot().Siyuan.Token);
    }
}
