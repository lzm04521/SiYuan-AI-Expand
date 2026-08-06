namespace SiYuanSync.Core.Sync;

public sealed record ScannedFile(string AbsolutePath, string RelPath);
public sealed record FileScanError(string Path, string Reason);
public sealed record ScanResult(IReadOnlyList<ScannedFile> Files, IReadOnlyList<FileScanError> Errors);

public sealed class PathNormalizerException : Exception
{
    public PathNormalizerException(string message) : base(message) { }
}
