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

    /// <summary>批量写入一轮中某项目的文件级明细（成功/跳过/失败全写，便于审计；查询时按需过滤）。</summary>
    void RecordFileDetails(string runId, string projectName, IEnumerable<FileResult> files);

    /// <summary>取最近一轮（runId）下 outcome != Success 的明细，按项目名/相对路径排序。</summary>
    IReadOnlyList<FileRunDetail> GetFailedOrSkipped(string runId);
}
