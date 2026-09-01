using SiYuanSync.Core.Models;
using SiYuanSync.Core.Siyuan;
using SiYuanSync.Core.Sync;
using Xunit;

namespace SiYuanSync.Core.Tests;

public class ParentDocInitializerTests
{
    private sealed class SpyClient : ISiyuanClient
    {
        public List<Notebook> Notebooks = new();
        public Dictionary<string, string[]> ByHPath = new();   // hpath → ids
        public List<string> CreatedHPaths = new();
        public bool AuthFail;
        public HashSet<string> CreateFailPaths = new();   // 对指定 hpath 创建时抛 SiyuanOperationException

        public Task<IReadOnlyList<Notebook>> ListNotebooksAsync(CancellationToken ct)
        { if (AuthFail) throw new SiyuanAuthException("401"); return Task.FromResult<IReadOnlyList<Notebook>>(Notebooks); }
        public Task<IReadOnlyList<string>> GetDocIdsByHPathAsync(string n, string h, CancellationToken ct)
        { if (AuthFail) throw new SiyuanAuthException("401"); return Task.FromResult<IReadOnlyList<string>>(ByHPath.TryGetValue(h, out var v) ? v : Array.Empty<string>()); }
        public Task<string> CreateDocWithMdAsync(string n, string h, string m, CancellationToken ct)
        {
            if (CreateFailPaths.Contains(h)) throw new SiyuanOperationException("标题非法");
            CreatedHPaths.Add(h);
            var id = $"doc-{CreatedHPaths.Count}";
            ByHPath[h] = new[] { id };
            return Task.FromResult(id);
        }
        public Task RenameDocByIdAsync(string id, string t, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveDocByIdAsync(string id, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<BlockChild>> GetChildBlocksAsync(string d, CancellationToken ct) => Task.FromResult<IReadOnlyList<BlockChild>>(Array.Empty<BlockChild>());
        public Task DeleteBlockAsync(string b, CancellationToken ct) => Task.CompletedTask;
        public Task PrependBlockAsync(string p, string m, CancellationToken ct) => Task.CompletedTask;
        public Task SetDocSortModeAsync(string d, int s, CancellationToken ct) => Task.CompletedTask;
    }

    private static ProjectConfig Project(string name = "JPT", string parent = "/JPT", string notebook = "AI") => new()
    { Name = name, Enabled = true, DocPath = @"C:\doc", Notebook = notebook, ParentPath = parent };

    [Fact]
    public async Task Absent_path_creates_each_segment_in_order()
    {
        var spy = new SpyClient { Notebooks = { new("n1", "AI") } };
        var r = await ParentDocInitializer.EnsureAsync(Project(parent: "/A/B/C"), "AI", spy, default);

        Assert.Equal(ParentInitStatus.Created, r.Status);
        Assert.Equal("doc-3", r.DocId);
        Assert.Equal(new[] { "/A", "/A/B", "/A/B/C" }, spy.CreatedHPaths);
    }

    [Fact]
    public async Task Partial_path_creates_only_missing_segments()
    {
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/A"] = new[] { "a1" } } };
        var r = await ParentDocInitializer.EnsureAsync(Project(parent: "/A/B/C"), "AI", spy, default);

        Assert.Equal(ParentInitStatus.Created, r.Status);
        Assert.Equal("doc-2", r.DocId);
        Assert.Equal(new[] { "/A/B", "/A/B/C" }, spy.CreatedHPaths);
    }

    [Fact]
    public async Task Existing_path_returns_Exists_without_create()
    {
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, ByHPath = { ["/JPT"] = new[] { "p1" } } };
        var r = await ParentDocInitializer.EnsureAsync(Project(), "AI", spy, default);

        Assert.Equal(ParentInitStatus.Exists, r.Status);
        Assert.Equal("p1", r.DocId);
        Assert.Empty(spy.CreatedHPaths);
    }

    [Fact]
    public async Task Notebook_missing_returns_Failed()
    {
        var spy = new SpyClient(); // 无任何笔记本
        var r = await ParentDocInitializer.EnsureAsync(Project(), "AI", spy, default);

        Assert.Equal(ParentInitStatus.Failed, r.Status);
        Assert.Contains("笔记本", r.Error);
    }

    [Fact]
    public async Task Empty_project_notebook_falls_back_to_default()
    {
        var spy = new SpyClient { Notebooks = { new("n1", "AI") } };
        var r = await ParentDocInitializer.EnsureAsync(Project(notebook: ""), "AI", spy, default);

        Assert.Equal(ParentInitStatus.Created, r.Status);
        Assert.Equal(new[] { "/JPT" }, spy.CreatedHPaths);
    }

    [Fact]
    public async Task Invalid_parentPath_returns_Failed()
    {
        var spy = new SpyClient { Notebooks = { new("n1", "AI") } };
        var r = await ParentDocInitializer.EnsureAsync(Project(parent: ""), "AI", spy, default);

        Assert.Equal(ParentInitStatus.Failed, r.Status);
        Assert.Contains("parentPath", r.Error);
    }

    [Fact]
    public async Task Batch_isolates_single_failure_and_continues()
    {
        // P1 笔记本不存在（Failed），P2 正常（Created）：P1 不得中断 P2
        var spy = new SpyClient { Notebooks = { new("n1", "AI") } };
        var results = await ParentDocInitializer.EnsureAllAsync(
            new[] { Project("P1", notebook: "Missing"), Project("P2", "/X") }, "AI", spy, default);

        Assert.Equal(2, results.Count);
        Assert.Equal(("P1", ParentInitStatus.Failed), (results[0].ProjectName, results[0].Status));
        Assert.Equal(("P2", ParentInitStatus.Created), (results[1].ProjectName, results[1].Status));
        Assert.Equal(new[] { "/X" }, spy.CreatedHPaths);
    }

    [Fact]
    public async Task Batch_create_failure_records_failed_and_continues()
    {
        // 思源端创建失败（如非法标题）：记 Failed 继续后续项目
        var spy = new SpyClient { Notebooks = { new("n1", "AI") }, CreateFailPaths = { "/A" } };
        var results = await ParentDocInitializer.EnsureAllAsync(
            new[] { Project("P1", "/A"), Project("P2", "/X") }, "AI", spy, default);

        Assert.Equal(("P1", ParentInitStatus.Failed), (results[0].ProjectName, results[0].Status));
        Assert.Contains("标题非法", results[0].Error);
        Assert.Equal(("P2", ParentInitStatus.Created), (results[1].ProjectName, results[1].Status));
    }

    [Fact]
    public async Task Auth_failure_propagates_from_batch()
    {
        var spy = new SpyClient { AuthFail = true };
        await Assert.ThrowsAsync<SiyuanAuthException>(
            () => ParentDocInitializer.EnsureAllAsync(new[] { Project() }, "AI", spy, default));
    }
}
