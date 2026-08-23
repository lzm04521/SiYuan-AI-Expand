using System.Text;

namespace SiYuanSync.Core.Sync;

public static class DocScanner
{
    public static ScanResult Scan(string docPath)
    {
        var files = new List<ScannedFile>();
        var errors = new List<FileScanError>();
        var seenHpathSuffix = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var abs in EnumerateSupportedFiles(docPath))
        {
            var rel = Path.GetRelativePath(docPath, abs).Replace('\\', '/');

            // 大小写不敏感同 hpath 冲突检测：以去支持后缀（.md/.html/.htm）、统一大小写的 rel 为键
            var key = SupportedFileTypes.StripSupportedExtension(rel);
            if (seenHpathSuffix.TryGetValue(key, out var first))
            {
                errors.Add(new FileScanError(abs, $"与 '{first}' 映射到同一思源 hpath（去后缀后同名冲突）"));
                continue;
            }
            seenHpathSuffix[key] = abs;

            files.Add(new ScannedFile(abs, rel));
        }

        // 按相对路径正序（大小写不敏感）：同步顺序稳定，浅路径先于深路径（'.' < '/'），父文档先建
        files.Sort((x, y) => string.Compare(x.RelPath, y.RelPath, StringComparison.OrdinalIgnoreCase));
        return new ScanResult(files, errors);
    }

    private static IEnumerable<string> EnumerateSupportedFiles(string root)
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
                if (SupportedFileTypes.IsSupportedExtension(fi.Extension))
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
