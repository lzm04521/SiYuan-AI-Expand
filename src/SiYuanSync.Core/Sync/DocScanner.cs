using System.Text;

namespace SiYuanSync.Core.Sync;

public static class DocScanner
{
    public static ScanResult Scan(string docPath)
    {
        var files = new List<ScannedFile>();
        var errors = new List<FileScanError>();
        var seenHpathSuffix = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var abs in EnumerateMarkdownFiles(docPath))
        {
            var rel = Path.GetRelativePath(docPath, abs).Replace('\\', '/');

            // 大小写不敏感同 hpath 冲突检测：以去后缀、统一大小写的 rel 为键
            var key = rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? rel[..^3] : rel;
            if (seenHpathSuffix.TryGetValue(key, out var first))
            {
                errors.Add(new FileScanError(abs, $"与 '{first}' 映射到同一思源 hpath（Windows 大小写不敏感冲突）"));
                continue;
            }
            seenHpathSuffix[key] = abs;

            files.Add(new ScannedFile(abs, rel));
        }
        return new ScanResult(files, errors);
    }

    private static IEnumerable<string> EnumerateMarkdownFiles(string root)
    {
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(root));
        while (stack.Count > 0)
        {
            DirectoryInfo dir;
            try { dir = stack.Pop(); }
            catch { continue; }

            IEnumerable<FileInfo> fis;
            try { fis = dir.EnumerateFiles(); }
            catch (UnauthorizedAccessException) { yield break; }
            catch { continue; }

            foreach (var fi in fis)
            {
                // 跳过符号链接/重解析点
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (fi.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
                    yield return fi.FullName;
            }

            DirectoryInfo[] subs;
            try { subs = dir.GetDirectories(); }
            catch { continue; }
            foreach (var sub in subs)
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                stack.Push(sub);
            }
        }
    }
}
