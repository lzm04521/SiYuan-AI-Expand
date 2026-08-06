using Microsoft.Data.Sqlite;

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

    public void Dispose() { }
}
