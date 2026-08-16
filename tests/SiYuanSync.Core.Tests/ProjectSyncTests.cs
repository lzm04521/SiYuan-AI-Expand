using Microsoft.Extensions.Logging.Abstractions;
using SiYuanSync.Core.Models;
using SiYuanSync.Core.State;
using SiYuanSync.Core.Siyuan;
using SiYuanSync.Core.Sync;
using Xunit;

namespace SiYuanSync.Core.Tests;

public class ProjectSyncTests : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;
    public ProjectSyncTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sye-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "state.db");
    }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private void Md(string rel, string content) { var f = Path.Combine(_root, "doc", rel); Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllText(f, content, System.Text.Encoding.UTF8); }

    private sealed class SpyClient : ISiyuanClient
    {
        public List<Notebook> Notebooks = new();
        public Dictionary<string, string[]> ByHPath = new();   // hpath → ids
        public List<string> CreatedHPaths = new();
        public bool AuthFailOnHPath; // 模拟认证失败
        public int HPathCallCount;

        public Task<IReadOnlyList<Notebook>> ListNotebooksAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Notebook>>(Notebooks);
        public Task<IReadOnlyList<string>> GetDocIdsByHPathAsync(string n, string h, CancellationToken ct)
        {
            HPathCallCount++;
            // 第一次调用是父目录校验，放行；后续调用（逐文件 upsert）抛鉴权异常
            if (AuthFailOnHPath && HPathCallCount > 1) throw new SiyuanAuthException("401");
            return Task.FromResult<IReadOnlyList<string>>(ByHPath.TryGetValue(h, out var v) ? v : Array.Empty<string>());
        }
        public Task<string> CreateDocWithMdAsync(string n, string h, string m, CancellationToken ct) { CreatedHPaths.Add(h); return Task.FromResult($"doc-{CreatedHPaths.Count}"); }
        public Task RenameDocByIdAsync(string id, string t, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveDocByIdAsync(string id, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<BlockChild>> GetChildBlocksAsync(string d, CancellationToken ct) => Task.FromResult<IReadOnlyList<BlockChild>>(Array.Empty<BlockChild>());
        public Task DeleteBlockAsync(string b, CancellationToken ct) => Task.CompletedTask;
        public Task PrependBlockAsync(string p, string m, CancellationToken ct) => Task.CompletedTask;
    }

    private ProjectConfig Project(string parent = "/JPT") => new()
    {
        Name = "JPT", Enabled = true, DocPath = Path.Combine(_root, "doc"),
        Notebook = "AI", ParentPath = parent
    };

    [Fact]
    public async Task First_sync_creates_doc_and_records_hash()
    {
        Md("a.md", "# A\n正文");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        var result = await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);

        Assert.Equal(RunStatus.Success, result.Status);
        Assert.Equal(1, result.Success);
        Assert.Contains("/JPT/a", spy.CreatedHPaths);
        Assert.NotNull(state.GetHash("JPT", "a.md"));
    }

    [Fact]
    public async Task First_sync_marks_outcome_Created()
    {
        Md("a.md", "# A\n正文");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        var result = await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        Assert.Equal(FileOutcome.Created, Assert.Single(result.Files, f => f.RelPath == "a.md").Outcome);
    }

    [Fact]
    public async Task Changed_content_marks_outcome_Updated()
    {
        Md("a.md", "# A\n正文");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);

        // 第二轮：内容变化 + 思源中已存在该 hpath（upsert 走更新路径）
        Md("a.md", "# A\n正文改");
        spy.ByHPath["/JPT/a"] = new[] { "doc-1" };
        var result = await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        Assert.Equal(FileOutcome.Updated, Assert.Single(result.Files, f => f.RelPath == "a.md").Outcome);
    }

    [Fact]
    public async Task Unchanged_md_skips_upsert()
    {
        Md("a.md", "# A\n正文");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        spy.CreatedHPaths.Clear();
        // 第二轮，文件不变
        await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        Assert.Empty(spy.CreatedHPaths);
    }

    [Fact]
    public async Task Parent_missing_marks_project_failed()
    {
        Md("a.md", "# A\n");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") } }; // /JPT 不存在
        using var state = new StateStore(_dbPath);
        var result = await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Contains("父目录", result.Error!);
        Assert.Empty(spy.CreatedHPaths);
    }

    [Fact]
    public async Task Notebook_missing_marks_project_failed()
    {
        Md("a.md", "# A\n");
        var spy = new SpyClient(); // AI 笔记本不存在
        using var state = new StateStore(_dbPath);
        var result = await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        Assert.Equal(RunStatus.Failed, result.Status);
    }

    [Fact]
    public async Task ParentPath_without_leading_slash_normalized_then_syncs()
    {
        // 回归：parentPath 缺前导 / 时曾导致"父目录不存在"死循环（init-parent 创建的是 /JPT，校验查的是 JPT）
        Md("a.md", "# A\n正文");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        var result = await ProjectSync.RunAsync(Project("JPT"), spy, state, NullLogger.Instance, default);
        Assert.Equal(RunStatus.Success, result.Status);
        Assert.Contains("/JPT/a", spy.CreatedHPaths);
    }

    [Fact]
    public async Task Empty_parentPath_marks_project_failed()
    {
        Md("a.md", "# A\n");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") } };
        using var state = new StateStore(_dbPath);
        var result = await ProjectSync.RunAsync(Project(""), spy, state, NullLogger.Instance, default);
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Contains("parentPath", result.Error!);
    }

    [Fact]
    public async Task Auth_failure_stops_project_and_marks_failed()
    {
        Md("a.md", "# A\n");
        Md("b.md", "# B\n");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } }, AuthFailOnHPath = true };
        using var state = new StateStore(_dbPath);
        var result = await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.True(result.Failed >= 1);
    }

    [Fact]
    public async Task Deleted_local_file_keeps_siyuan_doc()
    {
        Md("a.md", "# A\n");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        // 删本地
        File.Delete(Path.Combine(_root, "doc", "a.md"));
        spy.CreatedHPaths.Clear();
        var result = await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        Assert.Equal(RunStatus.Success, result.Status);
        Assert.Null(state.GetHash("JPT", "a.md")); // 状态清掉
        Assert.Empty(spy.CreatedHPaths); // 没有创建/删除调用
    }
}
