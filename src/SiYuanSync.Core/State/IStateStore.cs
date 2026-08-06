using SiYuanSync.Core.Models;

namespace SiYuanSync.Core.State;

public interface IStateStore : IDisposable
{
    string? GetHash(string projectName, string relPath);
    IReadOnlyList<string> ListRelsByProject(string projectName);
    void RecordFileSync(string projectName, string relPath, string hash, string? siyuanDocId, DateTime syncedAt);
    void DeleteFileSync(string projectName, string relPath);
    void RecordSyncRun(SyncRunRecord record);
    IReadOnlyList<SyncRunRecord> GetLatestRunByRunId();
}
