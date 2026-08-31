using System.Text;

namespace SiYuanSync.Core.Sync;

public static class DocScanner
{
    public static ScanResult Scan(string docPath, ScanFilter? filter = null)
    {
        var files = new List<ScannedFile>();
        var errors = new List<FileScanError>();
        var filtered = new List<FileScanError>();
        var deferred = new List<FileScanError>();
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenHpathSuffix = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 截断信号：任一目录枚举被吞错即置位，PresentRels 视为不完整（删除同步据此整项目跳过）
        bool truncated = false;
        foreach (var abs in EnumerateSupportedFiles(docPath, () => truncated = true))
        {
            var rel = Path.GetRelativePath(docPath, abs).Replace('\\', '/');
            present.Add(rel); // 磁盘全集：无论后续是否被过滤/冲突/延迟，本地存在即入集

            // 正则过滤（先 include 后 exclude）；被排除文件不参与冲突检测（不同步，不应挡住保留文件）
            if (filter?.Include is { } inc && !inc.IsMatch(rel))
            { filtered.Add(new FileScanError(abs, "不匹配 includePattern")); continue; }
            if (filter?.Exclude is { } exc && exc.IsMatch(rel))
            { filtered.Add(new FileScanError(abs, "命中 excludePattern")); continue; }

            // 大小写不敏感同 hpath 冲突检测：以去支持后缀（.md/.html/.htm）、统一大小写的 rel 为键
            var key = SupportedFileTypes.StripSupportedExtension(rel);
            if (seenHpathSuffix.TryGetValue(key, out var first))
            {
                errors.Add(new FileScanError(abs, $"与 '{first}' 映射到同一思源 hpath（去后缀后同名冲突）"));
                continue;
            }
            seenHpathSuffix[key] = abs;

            // 静默期：mtime 未满 → Deferred（已登记冲突键，静默满后参与同步）
            if (filter is { SettleMinutes: > 0 } sf)
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(abs);
                if (age < TimeSpan.FromMinutes(sf.SettleMinutes))
                {
                    var remain = (int)Math.Ceiling((TimeSpan.FromMinutes(sf.SettleMinutes) - age).TotalMinutes);
                    deferred.Add(new FileScanError(abs, $"未满静默期（剩余约 {remain} 分钟）"));
                    continue;
                }
            }

            files.Add(new ScannedFile(abs, rel));
        }

        // 按相对路径正序（大小写不敏感）：同步顺序稳定，浅路径先于深路径（'.' < '/'），父文档先建
        files.Sort((x, y) => string.Compare(x.RelPath, y.RelPath, StringComparison.OrdinalIgnoreCase));
        return new ScanResult(files, errors, filtered, deferred, present, truncated);
    }

    private static IEnumerable<string> EnumerateSupportedFiles(string root, Action onTruncate)
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
            catch (UnauthorizedAccessException) { onTruncate(); yield break; }
            catch { onTruncate(); continue; }

            foreach (var fi in fis)
            {
                // 跳过符号链接/重解析点
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (SupportedFileTypes.IsSupportedExtension(fi.Extension))
                    yield return fi.FullName;
            }

            DirectoryInfo[] subs;
            try { subs = dir.GetDirectories(); }
            catch { onTruncate(); continue; }
            foreach (var sub in subs)
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                stack.Push(sub);
            }
        }
    }
}
