using System.Text.RegularExpressions;

namespace SiYuanSync.Core.Sync;

public sealed record ScannedFile(string AbsolutePath, string RelPath);
public sealed record FileScanError(string Path, string Reason);

/// <summary>扫描过滤配置：正则已编译（匹配相对路径，/ 分隔），SettleMinutes=0 表示不启用静默期。</summary>
public sealed record ScanFilter(int SettleMinutes, Regex? Include, Regex? Exclude);

public sealed record ScanResult(
    IReadOnlyList<ScannedFile> Files,
    IReadOnlyList<FileScanError> Errors,
    IReadOnlyList<FileScanError> Filtered,   // 被正则排除：本地存在、不参与同步与冲突检测
    IReadOnlyList<FileScanError> Deferred,   // 未满静默期：本地存在、静默满后必然参与同步（参与冲突登记）
    IReadOnlySet<string> PresentRels);       // 本地受支持文件 rel 全集（Files∪Filtered∪Deferred∪冲突Errors），删除阶段判定依据

public sealed class PathNormalizerException : Exception
{
    public PathNormalizerException(string message) : base(message) { }
}
