namespace SiYuanSync.Core.Sync;

/// <summary>同步支持的文档后缀集合与派生判断（DocScanner 扫描过滤与 hpath 映射共用）。</summary>
public static class SupportedFileTypes
{
    public static readonly string[] Extensions = { ".md", ".html", ".htm" };

    public static bool IsSupportedExtension(string extension)
        => Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    public static bool IsHtml(string relPath)
        => relPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
        || relPath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

    public static string StripSupportedExtension(string relPath)
    {
        foreach (var ext in Extensions)
            if (relPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return relPath[..^ext.Length];
        return relPath;
    }
}
