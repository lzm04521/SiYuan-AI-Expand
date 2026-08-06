using System.Text;

namespace SiYuanSync.Core.Sync;

public static class PathNormalizer
{
    public static string NormalizeDocPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new PathNormalizerException("docPath 为空");
        string full;
        try { full = Path.GetFullPath(raw); }
        catch (Exception e) { throw new PathNormalizerException($"docPath 非法：{e.Message}"); }
        if (!Directory.Exists(full))
            throw new PathNormalizerException($"docPath 不存在或非目录：'{full}'");
        return full;
    }

    public static string RelPathToHPath(string parentPath, string relPath)
    {
        var parent = (parentPath ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(parent) || !parent.StartsWith('/'))
            throw new PathNormalizerException($"parentPath 必须以 / 开头：'{parentPath}'");

        // rel 去 .md 后缀（大小写不敏感），分隔统一 /
        var rel = relPath.Replace('\\', '/');
        if (rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            rel = rel[..^3];

        var segments = rel.Split('/');
        var sb = new StringBuilder(parent);
        foreach (var seg in segments)
            sb.Append('/').Append(NormalizeSegment(seg));
        return sb.ToString();
    }

    public static string NormalizeSegment(string segment)
    {
        var s = (segment ?? "").Trim();
        if (string.IsNullOrEmpty(s))
            throw new PathNormalizerException("hpath 段为空");
        if (s == "." || s == "..")
            throw new PathNormalizerException($"hpath 段非法：'{s}'");
        if (s.Any(ch => char.IsControl(ch)))
            throw new PathNormalizerException($"hpath 段含控制字符：'{s}'");
        return s;
    }
}
