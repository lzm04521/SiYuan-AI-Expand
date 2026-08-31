namespace SiYuanSync.Core.State;

internal static class StateSchema
{
    public const string FileSyncState = @"
CREATE TABLE IF NOT EXISTS file_sync_state (
  project_name TEXT NOT NULL,
  rel_path TEXT NOT NULL,
  content_hash TEXT NOT NULL,
  last_synced_at TEXT NOT NULL,
  siyuan_doc_id TEXT,
  PRIMARY KEY (project_name, rel_path)
);";

    public const string SyncRun = @"
CREATE TABLE IF NOT EXISTS sync_run (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  run_id TEXT NOT NULL,
  started_at TEXT NOT NULL,
  finished_at TEXT NOT NULL,
  project_name TEXT NOT NULL,
  success_count INTEGER NOT NULL,
  skipped_count INTEGER NOT NULL,
  failed_count INTEGER NOT NULL,
  deleted_count INTEGER NOT NULL DEFAULT 0,
  status TEXT NOT NULL,
  error TEXT
);
CREATE INDEX IF NOT EXISTS idx_sync_run_run_id ON sync_run(run_id);";

    public const string FileRunDetail = @"
CREATE TABLE IF NOT EXISTS file_run_detail (
  run_id TEXT NOT NULL,
  project_name TEXT NOT NULL,
  rel_path TEXT NOT NULL,
  outcome TEXT NOT NULL,
  error TEXT
);
CREATE INDEX IF NOT EXISTS idx_file_run_detail_run ON file_run_detail(run_id);";

    public const string EnableWal = "PRAGMA journal_mode=WAL;";
    public const string BusyTimeout = "PRAGMA busy_timeout=5000;";
}
