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
        new(runId, t, t.AddMinutes(1), project, s, sk, f, 0, status, null);

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

    // ============== ListSyncRuns（历史轮次查询） ==============

    [Fact]
    public void ListSyncRuns_orders_by_started_at_desc_and_groups_by_runId()
    {
        var t1 = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddHours(1);
        _store.RecordSyncRun(Rec("run-1", "A", RunStatus.Success, 1, 0, 0, t1));
        _store.RecordSyncRun(Rec("run-2", "A", RunStatus.Success, 1, 0, 0, t2));
        _store.RecordSyncRun(Rec("run-2", "B", RunStatus.Partial, 1, 0, 1, t2));

        var runs = _store.ListSyncRuns(20, 0);
        Assert.Equal(3, runs.Count);
        Assert.Equal("run-2", runs[0].RunId); // 最新轮在前，同轮内按项目名升序
        Assert.Equal("A", runs[0].ProjectName);
        Assert.Equal("B", runs[1].ProjectName);
        Assert.Equal("run-1", runs[2].RunId);
    }

    [Fact]
    public void ListSyncRuns_paginates_by_runId()
    {
        var t = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 1; i <= 3; i++)
            _store.RecordSyncRun(Rec($"run-{i}", "A", RunStatus.Success, 1, 0, 0, t.AddHours(i)));

        var page1 = _store.ListSyncRuns(2, 0);
        Assert.Equal(new[] { "run-3", "run-2" }, page1.Select(r => r.RunId));
        var page2 = _store.ListSyncRuns(2, 2);
        Assert.Equal(new[] { "run-1" }, page2.Select(r => r.RunId));
    }

    [Fact]
    public void ListSyncRuns_filters_by_project_and_returns_only_that_project_rows()
    {
        var t = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);
        _store.RecordSyncRun(Rec("run-1", "A", RunStatus.Success, 1, 0, 0, t));
        _store.RecordSyncRun(Rec("run-1", "B", RunStatus.Success, 1, 0, 0, t));
        _store.RecordSyncRun(Rec("run-2", "B", RunStatus.Success, 1, 0, 0, t.AddHours(1)));

        var runs = _store.ListSyncRuns(20, 0, project: "A");
        var only = Assert.Single(runs);
        Assert.Equal("run-1", only.RunId);
        Assert.Equal("A", only.ProjectName);
    }

    [Fact]
    public void ListSyncRuns_filters_by_half_open_date_range()
    {
        var d = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);
        _store.RecordSyncRun(Rec("run-1", "A", RunStatus.Success, 1, 0, 0, d));              // 8/6 00:00
        _store.RecordSyncRun(Rec("run-2", "A", RunStatus.Success, 1, 0, 0, d.AddHours(12))); // 8/6 12:00
        _store.RecordSyncRun(Rec("run-3", "A", RunStatus.Success, 1, 0, 0, d.AddDays(1)));   // 8/7 00:00（开区间上界，不含）

        var runs = _store.ListSyncRuns(20, 0, from: d, to: d.AddDays(1));
        Assert.Equal(new[] { "run-2", "run-1" }, runs.Select(r => r.RunId));
    }

    // ============== GetFileDetails（轮次全量明细） ==============

    private void SeedDetails(string runId) => _store.RecordFileDetails(runId, "A", new[]
    {
        new FileResult("created.md", FileOutcome.Created, null),
        new FileResult("updated.md", FileOutcome.Updated, null),
        new FileResult("skipped.md", FileOutcome.Skipped, null),
        new FileResult("failed.md", FileOutcome.Failed, "boom"),
    });

    [Fact]
    public void GetFileDetails_returns_all_outcomes_sorted()
    {
        SeedDetails("run-1");
        var details = _store.GetFileDetails("run-1");
        Assert.Equal(new[] { "created.md", "failed.md", "skipped.md", "updated.md" },
            details.Select(d => d.RelPath));
        Assert.Equal(FileOutcome.Created, details[0].Outcome);
    }

    [Fact]
    public void GetFailedOrSkipped_excludes_created_and_updated()
    {
        SeedDetails("run-1");
        var details = _store.GetFailedOrSkipped("run-1");
        Assert.Equal(new[] { "failed.md", "skipped.md" }, details.Select(d => d.RelPath));
    }

    [Fact]
    public void Legacy_success_outcome_still_parses_and_is_excluded_from_failed_or_skipped()
    {
        // 旧版本写入的 outcome='Success' 行：GetFileDetails 原样解析，GetFailedOrSkipped 不返回
        SeedDetails("run-1");
        using (var c = _store.OpenConnection())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE file_run_detail SET outcome='Success' WHERE rel_path='created.md'";
            cmd.ExecuteNonQuery();
        }

        var all = _store.GetFileDetails("run-1");
        Assert.Equal(FileOutcome.Success, all.Single(d => d.RelPath == "created.md").Outcome);
        Assert.DoesNotContain(_store.GetFailedOrSkipped("run-1"), d => d.RelPath == "created.md");
    }

    // ============== Deleted（删除同步计数与旧库迁移） ==============

    [Fact]
    public void Deleted_count_roundtrips()
    {
        var t = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc);
        _store.RecordSyncRun(new SyncRunRecord("run-d", t, t.AddMinutes(1), "A", 0, 0, 0, 2, RunStatus.Success, null));
        var latest = _store.GetLatestRunByRunId();
        Assert.Equal(2, Assert.Single(latest).DeletedCount);
        var listed = _store.ListSyncRuns(10, 0);
        Assert.Equal(2, Assert.Single(listed).DeletedCount);
    }

    [Fact]
    public void Old_db_without_deleted_count_column_migrates_on_open()
    {
        // 直接建旧结构库（无 deleted_count），再开 StateStore 触发迁移
        var oldDir = Path.Combine(Path.GetTempPath(), "sye-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(oldDir);
        try
        {
            var db = Path.Combine(oldDir, "state.db");
            using (var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
            {
                raw.Open();
                using var cmd = raw.CreateCommand();
                cmd.CommandText = @"CREATE TABLE sync_run (
              id INTEGER PRIMARY KEY AUTOINCREMENT, run_id TEXT NOT NULL, started_at TEXT NOT NULL,
              finished_at TEXT NOT NULL, project_name TEXT NOT NULL, success_count INTEGER NOT NULL,
              skipped_count INTEGER NOT NULL, failed_count INTEGER NOT NULL, status TEXT NOT NULL, error TEXT);";
                cmd.ExecuteNonQuery();
            }
            using (var store = new StateStore(db))   // 构造函数执行幂等迁移
            {
                var t = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc);
                store.RecordSyncRun(new SyncRunRecord("r", t, t, "A", 0, 0, 0, 1, RunStatus.Success, null));
                Assert.Equal(1, Assert.Single(store.GetLatestRunByRunId()).DeletedCount);
            }
        }
        finally { try { Directory.Delete(oldDir, true); } catch { } }
    }

    [Fact]
    public void GetFailedOrSkipped_includes_deleted_rows()
    {
        var t = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc);
        _store.RecordSyncRun(new SyncRunRecord("run-x", t, t, "A", 0, 0, 0, 0, RunStatus.Success, null));
        _store.RecordFileDetails("run-x", "A", new[]
        {
            new FileResult("gone.md", FileOutcome.Deleted, null),
            new FileResult("keep.md", FileOutcome.Skipped, "未满静默期（剩余约 3 分钟）"),
        });
        var rows = _store.GetFailedOrSkipped("run-x");
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Outcome == FileOutcome.Deleted);
    }
}
