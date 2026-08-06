using SiYuanSync.Core.Models;

namespace SiYuanSync.Core.Siyuan;

public sealed class RetryingSiyuanClient : ISiyuanClient
{
    private readonly ISiyuanClient _inner;
    private readonly int _maxAttempts;
    private readonly int _baseDelayMs;

    public RetryingSiyuanClient(ISiyuanClient inner, int maxAttempts = 3, int baseDelayMs = 500)
    { _inner = inner; _maxAttempts = maxAttempts; _baseDelayMs = baseDelayMs; }

    public Task<IReadOnlyList<Notebook>> ListNotebooksAsync(CancellationToken ct) =>
        Retry(() => _inner.ListNotebooksAsync(ct));
    public Task<IReadOnlyList<string>> GetDocIdsByHPathAsync(string n, string h, CancellationToken ct) =>
        Retry(() => _inner.GetDocIdsByHPathAsync(n, h, ct));
    public Task<string> CreateDocWithMdAsync(string n, string h, string m, CancellationToken ct) =>
        Retry(() => _inner.CreateDocWithMdAsync(n, h, m, ct));
    public Task RenameDocByIdAsync(string d, string t, CancellationToken ct) =>
        Retry(() => _inner.RenameDocByIdAsync(d, t, ct));
    public Task RemoveDocByIdAsync(string d, CancellationToken ct) =>
        Retry(() => _inner.RemoveDocByIdAsync(d, ct));
    public Task<IReadOnlyList<BlockChild>> GetChildBlocksAsync(string d, CancellationToken ct) =>
        Retry(() => _inner.GetChildBlocksAsync(d, ct));
    public Task DeleteBlockAsync(string b, CancellationToken ct) =>
        Retry(() => _inner.DeleteBlockAsync(b, ct));
    public Task PrependBlockAsync(string p, string m, CancellationToken ct) =>
        Retry(() => _inner.PrependBlockAsync(p, m, ct));

    private async Task<T> Retry<T>(Func<Task<T>> action)
    {
        int attempt = 0;
        while (true)
        {
            try { return await action(); }
            catch (OperationCanceledException) { throw; }
            catch (SiyuanAuthException) { throw; }
            catch (SiyuanOperationException) { throw; }
            catch (SiyuanTransientException)
            {
                attempt++;
                if (attempt >= _maxAttempts) throw;
                await Task.Delay(_baseDelayMs * (int)Math.Pow(2, attempt - 1));
            }
        }
    }

    private async Task Retry(Func<Task> action)
    {
        int attempt = 0;
        while (true)
        {
            try { await action(); return; }
            catch (OperationCanceledException) { throw; }
            catch (SiyuanAuthException) { throw; }
            catch (SiyuanOperationException) { throw; }
            catch (SiyuanTransientException)
            {
                attempt++;
                if (attempt >= _maxAttempts) throw;
                await Task.Delay(_baseDelayMs * (int)Math.Pow(2, attempt - 1));
            }
        }
    }
}
