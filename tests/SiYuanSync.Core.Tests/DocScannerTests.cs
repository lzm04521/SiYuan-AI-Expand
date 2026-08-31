using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using SiYuanSync.Core.Sync;

using Xunit;

namespace SiYuanSync.Core.Tests;

public class DocScannerTests : IDisposable
{
    private readonly string _root;
    public DocScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sye-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        // 默认 Windows NTFS 大小写不敏感：A.md 与 a.md 会互相覆盖，无法触发冲突检测。
        // 启用此目录的 case-sensitive 属性（best-effort，失败则冲突用例在测试内自动跳过）。
        TryEnableCaseSensitive(_root);
    }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Write(string rel, string content = "")
    {
        var full = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, System.Text.Encoding.UTF8);
        return full;
    }

    [Fact]
    public void Recurses_and_finds_md_case_insensitive()
    {
        Write("a.md");
        Write("sub/b.MD");
        Write("sub/deep/c.md");
        Write("ignore.txt");
        var result = DocScanner.Scan(_root);
        Assert.Equal(3, result.Files.Count);
        Assert.All(result.Files, f => Assert.EndsWith(".md", f.RelPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Case_insensitive_hpath_collision_recorded_as_error()
    {
        // Windows 大小写不敏感 → A.md 与 a.md 映射同 hpath
        Write("A.md", "A");
        Write("a.md", "a");
        // 若 FS 仍是大小写不敏感（两条写入指向同一文件），跳过本用例：无法在磁盘上构造冲突。
        if (File.ReadAllText(Path.Combine(_root, "A.md")) == "a")
            return;

        var result = DocScanner.Scan(_root);
        // 取其一进 Files，另一个进 Errors
        Assert.Single(result.Files);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Returns_rejects_directory_with_md_name()
    {
        Directory.CreateDirectory(Path.Combine(_root, "notamd.md"));
        Write("real.md");
        var result = DocScanner.Scan(_root);
        Assert.Single(result.Files);
    }

    [Fact]
    public void Files_sorted_by_relpath_ascending()
    {
        // DFS 子目录逆序深入（sub2 先于 sub 弹出），排序后应恢复路径正序
        Write("z.md");
        Write("sub2/y.md");
        Write("sub/x.md");
        Write("a.md");
        var result = DocScanner.Scan(_root);
        var rels = result.Files.Select(f => f.RelPath).ToArray();
        Assert.Equal(new[] { "a.md", "sub/x.md", "sub2/y.md", "z.md" }, rels);
    }

    [Fact]
    public void Scans_html_htm_and_md_ignores_others()
    {
        Write("a.md");
        Write("b.html");
        Write("c.htm");
        Write("d.HTML");
        Write("e.txt");
        var result = DocScanner.Scan(_root);
        Assert.Equal(4, result.Files.Count);
    }

    [Fact]
    public void Md_and_html_same_stem_conflicts_on_hpath()
    {
        // foo.md 与 foo.html 剥后缀后映射同一 hpath，按既有冲突机制报错
        Write("foo.md", "m");
        Write("foo.html", "h");
        var result = DocScanner.Scan(_root);
        Assert.Single(result.Files);
        Assert.NotEmpty(result.Errors);
    }

    private static ScanFilter Rx(string? inc = null, string? exc = null) =>
        new(0,
            inc is null ? null : new Regex(inc, RegexOptions.CultureInvariant),
            exc is null ? null : new Regex(exc, RegexOptions.CultureInvariant));

    [Fact]
    public void Include_pattern_filters_non_matching_into_Filtered()
    {
        Write("keep/a.md");
        Write("drop/b.md");
        var r = DocScanner.Scan(_root, Rx(inc: @"^keep/"));
        Assert.Single(r.Files, f => f.RelPath == "keep/a.md");
        var fe = Assert.Single(r.Filtered);
        Assert.Contains("includePattern", fe.Reason);
        Assert.Contains("drop/b.md", r.PresentRels);
    }

    [Fact]
    public void Exclude_pattern_hits_go_to_Filtered()
    {
        Write("a.md");
        Write("b.tmp.md");
        var r = DocScanner.Scan(_root, Rx(exc: @"\.tmp\.md$"));
        Assert.Single(r.Files, f => f.RelPath == "a.md");
        Assert.Single(r.Filtered, f => f.Reason.Contains("excludePattern"));
    }

    [Fact]
    public void Include_then_exclude_both_apply()
    {
        Write("keep/a.md");
        Write("keep/b.tmp.md");
        var r = DocScanner.Scan(_root, Rx(inc: @"^keep/", exc: @"\.tmp\.md$"));
        Assert.Single(r.Files);
        Assert.Single(r.Filtered);
    }

    [Fact]
    public void Regex_excluded_file_does_not_trigger_hpath_conflict()
    {
        // a.md 与 a.html 去后缀同名；a.md 被 include 排除后不构成冲突，a.html 正常收集
        Write("a.md");
        Write("a.html");
        var r = DocScanner.Scan(_root, Rx(inc: @"\.html$"));
        Assert.Empty(r.Errors);
        Assert.Single(r.Files, f => f.RelPath == "a.html");
    }

    [Fact]
    public void Deferred_file_still_registers_hpath_conflict()
    {
        Write("A.md"); Write("a.html");
        File.SetLastWriteTimeUtc(Path.Combine(_root, "A.md"), DateTime.UtcNow); // 未满静默
        var filter = new ScanFilter(10, null, null);
        var r = DocScanner.Scan(_root, filter);
        Assert.NotEmpty(r.Errors);           // 冲突仍暴露
        Assert.NotEmpty(r.Deferred);         // A.md 进 Deferred
    }

    [Fact]
    public void Recent_file_deferred_old_file_collected()
    {
        Write("old.md");
        Write("new.md");
        File.SetLastWriteTimeUtc(Path.Combine(_root, "old.md"), DateTime.UtcNow.AddMinutes(-30));
        File.SetLastWriteTimeUtc(Path.Combine(_root, "new.md"), DateTime.UtcNow); // 刚写
        var r = DocScanner.Scan(_root, new ScanFilter(10, null, null));
        Assert.Single(r.Files, f => f.RelPath == "old.md");
        var d = Assert.Single(r.Deferred);
        Assert.Contains("静默期", d.Reason);
        Assert.Contains("new.md", r.PresentRels);
    }

    [Fact]
    public void Settle_zero_or_null_filter_keeps_current_behavior()
    {
        Write("fresh.md"); // mtime=now
        Assert.Single(DocScanner.Scan(_root).Files);
        Assert.Single(DocScanner.Scan(_root, new ScanFilter(0, null, null)).Files);
    }

    [Fact]
    public void PresentRels_contains_conflict_files()
    {
        Write("A.md", "A"); Write("a.md", "a");
        // 若 FS 仍是大小写不敏感（两条写入指向同一文件），跳过本用例
        if (File.ReadAllText(Path.Combine(_root, "A.md")) == "a")
            return;
        var r = DocScanner.Scan(_root);
        Assert.NotEmpty(r.Errors);
        Assert.Contains("A.md", r.PresentRels);
        Assert.Contains("a.md", r.PresentRels);
    }

    private static void TryEnableCaseSensitive(string dir)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var psi = new ProcessStartInfo("fsutil.exe", $"file setCaseSensitiveInfo \"{dir}\" enable")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch
        {
            // 忽略：best-effort。失败时冲突用例自动跳过。
        }
    }
}
