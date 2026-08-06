namespace SiYuanSync.Core.Models;

public enum RunStatus { Success, Partial, Failed, Skipped, Cancelled }
public enum FileOutcome { Success, Skipped, Failed }

public sealed record SyncRunRecord(
    string RunId, DateTime StartedAt, DateTime FinishedAt, string ProjectName,
    int SuccessCount, int SkippedCount, int FailedCount, RunStatus Status, string? Error);

public sealed record FileResult(string RelPath, FileOutcome Outcome, string? Error);

public sealed record ProjectRunResult(
    string ProjectName, RunStatus Status,
    int Success, int Skipped, int Failed,
    IReadOnlyList<FileResult> Files, string? Error);

public sealed record SyncRunResult(
    string RunId, DateTime StartedAt, DateTime FinishedAt,
    IReadOnlyList<ProjectRunResult> Projects);
