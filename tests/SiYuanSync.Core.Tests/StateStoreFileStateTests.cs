using SiYuanSync.Core.State;

using Xunit;

namespace SiYuanSync.Core.Tests;

public class StateStoreFileStateTests : IDisposable
{
    private readonly StateStore _store;
    private readonly string _dir;
    public StateStoreFileStateTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sye-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new StateStore(Path.Combine(_dir, "state.db"));
    }
    public void Dispose() { _store.Dispose(); try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void GetHash_unknown_returns_null()
        => Assert.Null(_store.GetHash("JPT", "a.md"));

    [Fact]
    public void RecordFileSync_then_GetHash_roundtrips()
    {
        var at = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        _store.RecordFileSync("JPT", "a.md", "hash1", "doc-1", at);
        Assert.Equal("hash1", _store.GetHash("JPT", "a.md"));
    }

    [Fact]
    public void RecordFileSync_upsert_updates_hash()
    {
        _store.RecordFileSync("JPT", "a.md", "h1", "d1", DateTime.UtcNow);
        _store.RecordFileSync("JPT", "a.md", "h2", "d2", DateTime.UtcNow);
        Assert.Equal("h2", _store.GetHash("JPT", "a.md"));
    }

    [Fact]
    public void DeleteFileSync_removes_record()
    {
        _store.RecordFileSync("JPT", "a.md", "h1", "d1", DateTime.UtcNow);
        _store.DeleteFileSync("JPT", "a.md");
        Assert.Null(_store.GetHash("JPT", "a.md"));
    }

    [Fact]
    public void State_is_isolated_per_project()
    {
        _store.RecordFileSync("JPT", "a.md", "h", "d", DateTime.UtcNow);
        Assert.Null(_store.GetHash("OTHER", "a.md"));
    }
}
