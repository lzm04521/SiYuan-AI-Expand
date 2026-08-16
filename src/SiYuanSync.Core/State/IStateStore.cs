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

    /// <summary>历史轮次查询：按 runId 去重、started_at 倒序分页，返回该批 runId 的全部项目行。from/to 为半开区间 [from, to)（UTC），project 为空不过滤。</summary>
    IReadOnlyList<SyncRunRecord> ListSyncRuns(int limit, int offset, string? project = null, DateTime? from = null, DateTime? to = null);

    /// <summary>取某轮全量文件明细（含成功文件），按项目名/相对路径排序。</summary>
    IReadOnlyList<FileRunDetail> GetFileDetails(string runId);
}
