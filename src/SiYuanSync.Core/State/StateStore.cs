using Microsoft.Data.Sqlite;
using SiYuanSync.Core.Models;

namespace SiYuanSync.Core.State;

public sealed class StateStore : IStateStore
{
    private readonly string _connStr;

    public StateStore(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        using var c = OpenConnection();
        using (var cmd = c.CreateCommand()) { cmd.CommandText = StateSchema.EnableWal; cmd.ExecuteNonQuery(); }
        using (var cmd = c.CreateCommand()) { cmd.CommandText = StateSchema.BusyTimeout; cmd.ExecuteNonQuery(); }
        using (var cmd = c.CreateCommand()) { cmd.CommandText = StateSchema.FileSyncState; cmd.ExecuteNonQuery(); }
        using (var cmd = c.CreateCommand()) { cmd.CommandText = StateSchema.SyncRun; cmd.ExecuteNonQuery(); }
        using (var cmd = c.CreateCommand()) { cmd.CommandText = StateSchema.FileRunDetail; cmd.ExecuteNonQuery(); }
    }

    internal SqliteConnection OpenConnection()
    {
        var c = new SqliteConnection(_connStr);
        c.Open();
        using (var cmd = c.CreateCommand()) { cmd.CommandText = StateSchema.BusyTimeout; cmd.ExecuteNonQuery(); }
        return c;
    }

    public string? GetHash(string projectName, string relPath)
    {
        using var c = OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT content_hash FROM file_sync_state WHERE project_name=@p AND rel_path=@r";
        cmd.Parameters.AddWithValue("@p", projectName);
        cmd.Parameters.AddWithValue("@r", relPath);
        return cmd.ExecuteScalar() as string;
    }

    public IReadOnlyList<string> ListRelsByProject(string projectName)
    {
        using var c = OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT rel_path FROM file_sync_state WHERE project_name=@p";
        cmd.Parameters.AddWithValue("@p", projectName);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(r.GetString(0));
        return list;
    }

    public void RecordFileSync(string projectName, string relPath, string hash, string? siyuanDocId, DateTime syncedAt)
    {
        using var c = OpenConnection();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO file_sync_state (project_name, rel_path, content_hash, last_synced_at, siyuan_doc_id)
VALUES (@p, @r, @h, @t, @d)
ON CONFLICT(project_name, rel_path) DO UPDATE SET
  content_hash=excluded.content_hash,
  last_synced_at=excluded.last_synced_at,
  siyuan_doc_id=excluded.siyuan_doc_id;";
        cmd.Parameters.AddWithValue("@p", projectName);
        cmd.Parameters.AddWithValue("@r", relPath);
        cmd.Parameters.AddWithValue("@h", hash);
        cmd.Parameters.AddWithValue("@t", syncedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@d", (object?)siyuanDocId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public void DeleteFileSync(string projectName, string relPath)
    {
        using var c = OpenConnection();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM file_sync_state WHERE project_name=@p AND rel_path=@r";
        cmd.Parameters.AddWithValue("@p", projectName);
        cmd.Parameters.AddWithValue("@r", relPath);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public void RecordSyncRun(SyncRunRecord r)
    {
        using var c = OpenConnection();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO sync_run (run_id, started_at, finished_at, project_name,
  success_count, skipped_count, failed_count, status, error)
VALUES (@run,@s,@f,@p,@sc,@sk,@fc,@st,@e);";
        cmd.Parameters.AddWithValue("@run", r.RunId);
        cmd.Parameters.AddWithValue("@s", r.StartedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@f", r.FinishedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@p", r.ProjectName);
        cmd.Parameters.AddWithValue("@sc", r.SuccessCount);
        cmd.Parameters.AddWithValue("@sk", r.SkippedCount);
        cmd.Parameters.AddWithValue("@fc", r.FailedCount);
        cmd.Parameters.AddWithValue("@st", r.Status.ToString());
        cmd.Parameters.AddWithValue("@e", (object?)r.Error ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public IReadOnlyList<SyncRunRecord> GetLatestRunByRunId()
    {
        using var c = OpenConnection();
        // 取 started_at 最大那条的 run_id，再取该 run_id 全部记录
        using var latestCmd = c.CreateCommand();
        latestCmd.CommandText = "SELECT run_id FROM sync_run ORDER BY started_at DESC LIMIT 1";
        var runId = latestCmd.ExecuteScalar() as string;
        if (runId is null) return Array.Empty<SyncRunRecord>();

        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT run_id, started_at, finished_at, project_name, success_count, skipped_count, failed_count, status, error FROM sync_run WHERE run_id=@run ORDER BY project_name";
        cmd.Parameters.AddWithValue("@run", runId);
        var list = new List<SyncRunRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new SyncRunRecord(
                r.GetString(0),
                DateTime.Parse(r.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                DateTime.Parse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
                r.GetString(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6),
                Enum.Parse<RunStatus>(r.GetString(7)),
                r.IsDBNull(8) ? null : r.GetString(8)));
        }
        return list;
    }

    public void RecordFileDetails(string runId, string projectName, IEnumerable<FileResult> files)
    {
        using var c = OpenConnection();
        using var tx = c.BeginTransaction();
        foreach (var f in files)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO file_run_detail (run_id, project_name, rel_path, outcome, error)
VALUES (@run, @p, @r, @o, @e);";
            cmd.Parameters.AddWithValue("@run", runId);
            cmd.Parameters.AddWithValue("@p", projectName);
            cmd.Parameters.AddWithValue("@r", f.RelPath);
            cmd.Parameters.AddWithValue("@o", f.Outcome.ToString());
            cmd.Parameters.AddWithValue("@e", (object?)f.Error ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<FileRunDetail> GetFailedOrSkipped(string runId)
    {
        using var c = OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT project_name, rel_path, outcome, error FROM file_run_detail
WHERE run_id=@run AND outcome <> @success
ORDER BY project_name, rel_path;";
        cmd.Parameters.AddWithValue("@run", runId);
        cmd.Parameters.AddWithValue("@success", FileOutcome.Success.ToString());
        var list = new List<FileRunDetail>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new FileRunDetail(
                r.GetString(0),
                r.GetString(1),
                Enum.Parse<FileOutcome>(r.GetString(2)),
                r.IsDBNull(3) ? null : r.GetString(3)));
        }
        return list;
    }

    public void Dispose() { }
}
