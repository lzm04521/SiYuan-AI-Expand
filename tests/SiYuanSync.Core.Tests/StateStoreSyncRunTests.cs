using SiYuanSync.Core.Models;
using SiYuanSync.Core.State;

using Xunit;

namespace SiYuanSync.Core.Tests;

public class StateStoreSyncRunTests : IDisposable
{
    private readonly StateStore _store;
    private readonly string _dir;
    public StateStoreSyncRunTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sye-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new StateStore(Path.Combine(_dir, "state.db"));
    }
    public void Dispose() { _store.Dispose(); try { Directory.Delete(_dir, true); } catch { } }

    private static SyncRunRecord Rec(string runId, string project, RunStatus status, int s, int sk, int f, DateTime t) =>
        new(runId, t, t.AddMinutes(1), project, s, sk, f, status, null);

    [Fact]
    public void Multiple_projects_same_runId_returned_together()
    {
        var t = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);
        _store.RecordSyncRun(Rec("run-1", "A", RunStatus.Success, 3, 0, 0, t));
        _store.RecordSyncRun(Rec("run-1", "B", RunStatus.Partial, 1, 0, 1, t));

        var latest = _store.GetLatestRunByRunId();
        Assert.Equal(2, latest.Count);
        Assert.All(latest, r => Assert.Equal("run-1", r.RunId));
    }

    [Fact]
    public void Only_latest_runId_returned()
    {
        var t1 = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddHours(1);
        _store.RecordSyncRun(Rec("run-1", "A", RunStatus.Success, 1, 0, 0, t1));
        _store.RecordSyncRun(Rec("run-2", "A", RunStatus.Failed, 0, 0, 1, t2));

        var latest = _store.GetLatestRunByRunId();
        Assert.Single(latest);
        Assert.Equal("run-2", latest[0].RunId);
    }

    [Fact]
    public void Empty_when_no_runs()
        => Assert.Empty(_store.GetLatestRunByRunId());
}
