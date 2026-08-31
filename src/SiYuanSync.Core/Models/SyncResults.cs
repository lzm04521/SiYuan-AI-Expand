namespace SiYuanSync.Core.Models;

public enum RunStatus { Success, Partial, Failed, Skipped, Cancelled }
/// <summary>Success 为旧数据兼容值（历史行 outcome='Success' 原样解析），新写入的成功结果用 Created/Updated 区分；Deleted 表示本地删除触发思源侧删除。</summary>
public enum FileOutcome { Success, Created, Updated, Skipped, Failed, Deleted }

public sealed record SyncRunRecord(
    string RunId, DateTime StartedAt, DateTime FinishedAt, string ProjectName,
    int SuccessCount, int SkippedCount, int FailedCount, int DeletedCount, RunStatus Status, string? Error);

public sealed record FileResult(string RelPath, FileOutcome Outcome, string? Error);

/// <summary>file_run_detail 行：FileResult + 所属项目名（GET /api/status 用于按项目分组展示明细）。</summary>
public sealed record FileRunDetail(string ProjectName, string RelPath, FileOutcome Outcome, string? Error);

public sealed record ProjectRunResult(
    string ProjectName, RunStatus Status,
    int Success, int Skipped, int Failed, int Deleted,
    IReadOnlyList<FileResult> Files, string? Error);

public sealed record SyncRunResult(
    string RunId, DateTime StartedAt, DateTime FinishedAt,
    IReadOnlyList<ProjectRunResult> Projects);
