using System.Diagnostics;
using System.Runtime.InteropServices;

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
