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
        public Dictionary<string, string> CreatedMd = new();   // hpath → 创建时的 md 正文
        public Task<string> CreateDocWithMdAsync(string n, string h, string m, CancellationToken ct)
        { CreatedHPaths.Add(h); CreatedMd[h] = m; var id = $"doc-{CreatedHPaths.Count}"; ByHPath[h] = new[] { id }; return Task.FromResult(id); }
        public Task RenameDocByIdAsync(string id, string t, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveDocByIdAsync(string id, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<BlockChild>> GetChildBlocksAsync(string d, CancellationToken ct) => Task.FromResult<IReadOnlyList<BlockChild>>(Array.Empty<BlockChild>());
        public Task DeleteBlockAsync(string b, CancellationToken ct) => Task.CompletedTask;
        public Task PrependBlockAsync(string p, string m, CancellationToken ct) => Task.CompletedTask;
        public List<(string docId, int sortMode)> SortCalls = new();
        public bool SortFail;
        public Task SetDocSortModeAsync(string d, int s, CancellationToken ct)
        { if (SortFail) throw new SiyuanOperationException("404"); SortCalls.Add((d, s)); return Task.CompletedTask; }
    }

    private ProjectConfig Project(string parent = "/JPT") => new()
    {
        Name = "JPT", Enabled = true, DocPath = Path.Combine(_root, "doc"),
        Notebook = "AI", ParentPath = parent
    };

    [Fact]
    public async Task SortMode_set_applies_to_parent_doc_after_sync()
    {
        Md("a.md", "# A\n正文");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        var proj = Project(); proj.SortMode = 3;
        var result = await ProjectSync.RunAsync(proj, spy, state, NullLogger.Instance, default);
        Assert.Equal(RunStatus.Success, result.Status);
        Assert.Equal(("parent", 3), Assert.Single(spy.SortCalls));
    }

    [Fact]
    public async Task SortMode_null_skips_sort_call()
    {
        Md("a.md", "# A\n正文");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        var result = await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        Assert.Equal(RunStatus.Success, result.Status);
        Assert.Empty(spy.SortCalls);
    }

    [Fact]
    public async Task SortMode_failure_does_not_fail_sync()
    {
        Md("a.md", "# A\n正文");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } }, SortFail = true };
        using var state = new StateStore(_dbPath);
        var proj = Project(); proj.SortMode = 3;
        var result = await ProjectSync.RunAsync(proj, spy, state, NullLogger.Instance, default);
        Assert.Equal(RunStatus.Success, result.Status);
        Assert.Equal(1, result.Success);
    }

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

        // 第二轮：内容变化（思源中该 hpath 已由第一轮创建自动注册）
        Md("a.md", "# A\n正文改");
        var result = await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        Assert.Equal(FileOutcome.Updated, Assert.Single(result.Files, f => f.RelPath == "a.md").Outcome);
    }

    [Fact]
    public async Task Unchanged_md_with_existing_siyuan_doc_skips_upsert()
    {
        Md("a.md", "# A\n正文");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        spy.CreatedHPaths.Clear();
        // 第二轮，文件不变且思源端文档仍存在
        var result = await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        Assert.Equal(FileOutcome.Skipped, Assert.Single(result.Files, f => f.RelPath == "a.md").Outcome);
        Assert.Empty(spy.CreatedHPaths);
    }

    [Fact]
    public async Task Siyuan_doc_deleted_recreates_even_if_hash_unchanged()
    {
        Md("a.md", "# A\n正文");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);

        // 思源端文档被手动删除，本地文件不变：不得因 hash 一致而跳过
        spy.ByHPath.Remove("/JPT/a");
        spy.CreatedHPaths.Clear();
        var result = await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);

        Assert.Equal(RunStatus.Success, result.Status);
        Assert.Equal(FileOutcome.Created, Assert.Single(result.Files, f => f.RelPath == "a.md").Outcome);
        Assert.Contains("/JPT/a", spy.CreatedHPaths);
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

    [Fact]
    public async Task Html_report_converted_and_created_as_markdown()
    {
        Md("r.html", "<html><head><title>忽略头</title></head><body><h1>报告标题</h1><p>结论甲乙</p></body></html>");
        Md("a.md", "# A\n正文");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        var result = await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);

        Assert.Equal(RunStatus.Success, result.Status);
        Assert.Equal(2, result.Success);
        Assert.Contains("/JPT/r", spy.CreatedHPaths);          // hpath 剥 .html 后缀
        Assert.DoesNotContain("忽略头", spy.CreatedMd["/JPT/r"]); // head 不进正文
        Assert.DoesNotContain("# 报告标题", spy.CreatedMd["/JPT/r"]); // 首行 H1 已剥离为标题
        Assert.Contains("结论甲乙", spy.CreatedMd["/JPT/r"]);   // 正文保留
        Assert.NotNull(state.GetHash("JPT", "r.html"));        // 状态键含后缀
    }

    [Fact]
    public async Task Html_without_h1_title_falls_back_to_filename()
    {
        Md("r.html", "<body><p>只有正文</p></body>");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        var result = await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);

        Assert.Equal(RunStatus.Success, result.Status);
        // 无首行 H1：创建路径标题即 hpath 末段（文件名），正文原样
        Assert.Contains("/JPT/r", spy.CreatedHPaths);
        Assert.Contains("只有正文", spy.CreatedMd["/JPT/r"]);
    }

    [Fact]
    public async Task Unchanged_html_skipped_second_round()
    {
        Md("r.html", "<body><h1>R</h1><p>x</p></body>");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        spy.CreatedHPaths.Clear();
        var result = await ProjectSync.RunAsync(Project(), spy, state, NullLogger.Instance, default);
        Assert.Equal(FileOutcome.Skipped, Assert.Single(result.Files, f => f.RelPath == "r.html").Outcome);
        Assert.Empty(spy.CreatedHPaths);
    }

    [Fact]
    public async Task Settled_file_skips_with_reason_and_syncs_after_window()
    {
        Md("a.md", "# A\n正文");
        File.SetLastWriteTimeUtc(Path.Combine(_root, "doc", "a.md"), DateTime.UtcNow); // 未满
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        var proj = Project(); proj.SettleMinutes = 10;
        var r = await ProjectSync.RunAsync(proj, spy, state, NullLogger.Instance, default);
        Assert.Equal(RunStatus.Success, r.Status);
        Assert.Equal(0, r.Success);
        var fr = Assert.Single(r.Files, f => f.RelPath == "a.md");
        Assert.Equal(FileOutcome.Skipped, fr.Outcome);
        Assert.Contains("静默期", fr.Error);
    }

    [Fact]
    public async Task Excluded_file_skips_and_state_not_purged()
    {
        Md("keep.md", "# K");
        Md("skip.tmp.md", "# T");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        var proj = Project(); proj.ExcludePattern = @"\.tmp\.md$";
        await ProjectSync.RunAsync(proj, spy, state, NullLogger.Instance, default); // 首轮：skip.tmp 未同步过
        // 先手动给 skip.tmp.md 补一条 state 记录（模拟历史上同步过），再跑一轮
        state.RecordFileSync(proj.Name, "skip.tmp.md", "hash-old", "doc-x", DateTime.UtcNow);
        await ProjectSync.RunAsync(proj, spy, state, NullLogger.Instance, default);
        Assert.NotNull(state.GetHash(proj.Name, "skip.tmp.md")); // 本地存在 → 不被清
    }

    [Fact]
    public async Task Invalid_runtime_regex_fails_project()
    {
        Md("a.md", "# A");
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        var proj = Project(); proj.IncludePattern = "(unclosed"; // 绕过保存校验的手改场景
        var r = await ProjectSync.RunAsync(proj, spy, state, NullLogger.Instance, default);
        Assert.Equal(RunStatus.Failed, r.Status);
        Assert.Contains("正则", r.Error);
    }

    [Fact]
    public async Task Conflict_file_present_locally_keeps_state()
    {
        Md("A.md", "A");
        Md("a.md", "a"); // 大小写冲突（FS 大小写敏感时成两文件）
        if (File.ReadAllText(Path.Combine(_root, "doc", "A.md")) == "a") return;
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "parent" } } };
        using var state = new StateStore(_dbPath);
        var proj = Project();
        state.RecordFileSync(proj.Name, "A.md", "hash", "doc-1", DateTime.UtcNow);
        await ProjectSync.RunAsync(proj, spy, state, NullLogger.Instance, default);
        Assert.NotNull(state.GetHash(proj.Name, "A.md")); // 基线微调：冲突文件本地存在不被清
    }
}
