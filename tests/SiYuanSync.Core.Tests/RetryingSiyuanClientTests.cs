using SiYuanSync.Core.Models;
using SiYuanSync.Core.Siyuan;
using Xunit;

namespace SiYuanSync.Core.Tests;

public class RetryingSiyuanClientTests
{
    private sealed class FakeClient : ISiyuanClient
    {
        private readonly Queue<Exception?> _behaviors = new();
        public int Calls;
        public FakeClient(params Exception?[] behaviors) { foreach (var b in behaviors) _behaviors.Enqueue(b); }
        public Task<IReadOnlyList<Notebook>> ListNotebooksAsync(CancellationToken ct) => Next<IReadOnlyList<Notebook>>(Array.Empty<Notebook>());
        public Task<IReadOnlyList<string>> GetDocIdsByHPathAsync(string n, string h, CancellationToken ct) => Next<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<string> CreateDocWithMdAsync(string n, string h, string m, CancellationToken ct) => Next("doc");
        public Task RenameDocByIdAsync(string d, string t, CancellationToken ct) => Next<bool>(true);
        public Task RemoveDocByIdAsync(string d, CancellationToken ct) => Next<bool>(true);
        public Task<IReadOnlyList<BlockChild>> GetChildBlocksAsync(string d, CancellationToken ct) => Next<IReadOnlyList<BlockChild>>(Array.Empty<BlockChild>());
        public Task DeleteBlockAsync(string b, CancellationToken ct) => Next<bool>(true);
        public Task PrependBlockAsync(string p, string m, CancellationToken ct) => Next<bool>(true);
        public Task SetDocSortModeAsync(string d, int s, CancellationToken ct) => Task.CompletedTask;

        private async Task<T> Next<T>(T ok)
        {
            Calls++;
            await Task.Yield();
            if (_behaviors.Count > 0)
            {
                var ex = _behaviors.Dequeue();
                if (ex is not null) throw ex;
            }
            return ok;
        }
    }

    private static RetryingSiyuanClient Wrap(FakeClient inner, int max = 3, int baseMs = 1) =>
        new(inner, maxAttempts: max, baseDelayMs: baseMs);

    [Fact]
    public async Task Transient_then_success_succeeds()
    {
        var inner = new FakeClient(new SiyuanTransientException("timeout"));
        var c = Wrap(inner);
        await c.ListNotebooksAsync(default);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Transient_exceeding_attempts_throws_transient()
    {
        var inner = new FakeClient(
            new SiyuanTransientException("e1"),
            new SiyuanTransientException("e2"),
            new SiyuanTransientException("e3"));
        var c = Wrap(inner, max: 3);
        await Assert.ThrowsAsync<SiyuanTransientException>(() => c.ListNotebooksAsync(default));
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task Auth_exception_not_retried()
    {
        var inner = new FakeClient(new SiyuanAuthException("401"));
        var c = Wrap(inner);
        await Assert.ThrowsAsync<SiyuanAuthException>(() => c.ListNotebooksAsync(default));
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Operation_exception_not_retried()
    {
        var inner = new FakeClient(new SiyuanOperationException("boom"));
        var c = Wrap(inner);
        await Assert.ThrowsAsync<SiyuanOperationException>(() => c.ListNotebooksAsync(default));
        Assert.Equal(1, inner.Calls);
    }
}
