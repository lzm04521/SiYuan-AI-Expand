using SiYuanSync.Core.Models;
using SiYuanSync.Core.Siyuan;
using SiYuanSync.Core.Sync;
using Xunit;

namespace SiYuanSync.Core.Tests;

public class DocUpsertTests
{
    private sealed class SpyClient : ISiyuanClient
    {
        public List<string> Created = new();
        public List<string> Renamed = new();
        public List<string> Removed = new();
        public List<string> DeletedBlocks = new();
        public List<string> Prepended = new();
        public Dictionary<string, IReadOnlyList<string>> Existing = new();   // hpath → docIds
        public Dictionary<string, IReadOnlyList<BlockChild>> Children = new(); // docId → blocks
        public int CreateCallIndex;
        public bool FailingPrepend;

        public Task<IReadOnlyList<Notebook>> ListNotebooksAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetDocIdsByHPathAsync(string n, string h, CancellationToken ct)
            => Task.FromResult(Existing.TryGetValue(h, out var v) ? v : (IReadOnlyList<string>)Array.Empty<string>());
        public Task<string> CreateDocWithMdAsync(string n, string h, string m, CancellationToken ct)
        { var id = $"new-{Created.Count}"; Created.Add((h, id).ToString()); CreateCallIndex++; return Task.FromResult(id); }
        public Task RenameDocByIdAsync(string id, string t, CancellationToken ct) { Renamed.Add(id); return Task.CompletedTask; }
        public Task RemoveDocByIdAsync(string id, CancellationToken ct) { Removed.Add(id); return Task.CompletedTask; }
        public Task<IReadOnlyList<BlockChild>> GetChildBlocksAsync(string id, CancellationToken ct)
            => Task.FromResult(Children.TryGetValue(id, out var v) ? v : (IReadOnlyList<BlockChild>)Array.Empty<BlockChild>());
        public Task DeleteBlockAsync(string b, CancellationToken ct) { DeletedBlocks.Add(b); return Task.CompletedTask; }
        public Task SetDocSortModeAsync(string d, int s, CancellationToken ct) => Task.CompletedTask;
        public Task PrependBlockAsync(string p, string m, CancellationToken ct)
        {
            if (FailingPrepend) throw new SiyuanOperationException("prepend failed");
            Prepended.Add(p); return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Absent_creates_doc()
    {
        var spy = new SpyClient();
        var r = await DocUpsert.UpsertAsync(spy, "n1", "/JPT/x", "正文", "x", default);
        Assert.Equal(UpsertMode.Created, r.Mode);
        Assert.NotEmpty(spy.Created);
        Assert.Empty(spy.Prepended);
    }

    [Fact]
    public async Task Existing_runs_U2_rename_delete_prepend()
    {
        var spy = new SpyClient
        {
            Existing = { ["/JPT/x"] = new[] { "doc-1" } },
            Children = { ["doc-1"] = new[] { new BlockChild("b1", "NodeParagraph"), new BlockChild("b2", "NodeHeading") } }
        };
        var r = await DocUpsert.UpsertAsync(spy, "n1", "/JPT/x", "新正文", "新标题", default);
        Assert.Equal(UpsertMode.Updated, r.Mode);
        Assert.Contains("doc-1", spy.Renamed);
        Assert.Equal(new[] { "b1", "b2" }, spy.DeletedBlocks.ToArray());
        Assert.Contains("doc-1", spy.Prepended);
        Assert.Empty(spy.Removed);
    }

    [Fact]
    public async Task U2_failure_falls_back_to_U1_rebuild()
    {
        var spy = new SpyClient
        {
            Existing = { ["/JPT/x"] = new[] { "doc-1" } }
        };
        // 让 PrependBlock 抛一次业务异常 → 走 U1
        spy.FailingPrepend = true;
        var r = await DocUpsert.UpsertAsync(spy, "n1", "/JPT/x", "正文", "t", default);
        Assert.Equal(UpsertMode.Rebuilt, r.Mode);
        Assert.Contains("doc-1", spy.Removed);
        Assert.NotEmpty(spy.Created);
    }
}
