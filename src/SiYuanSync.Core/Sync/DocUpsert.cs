using SiYuanSync.Core.Siyuan;

namespace SiYuanSync.Core.Sync;

public enum UpsertMode { Created, Updated, Rebuilt }
public sealed record UpsertResult(string DocId, UpsertMode Mode);

public sealed class DocUpsertException : Exception
{
    public string Stage { get; }
    public string? DocId { get; }
    public DocUpsertException(string stage, string? docId, Exception inner)
        : base($"upsert 阶段 '{stage}' 失败，docId={docId}", inner) { Stage = stage; DocId = docId; }
}

public static class DocUpsert
{
    public static async Task<UpsertResult> UpsertAsync(
        ISiyuanClient siyuan, string notebookId, string hpath, string bodyMd, string title, CancellationToken ct)
    {
        var ids = await siyuan.GetDocIdsByHPathAsync(notebookId, hpath, ct);

        if (ids.Count == 0)
        {
            string newId;
            try { newId = await siyuan.CreateDocWithMdAsync(notebookId, hpath, bodyMd, ct); }
            catch (Exception e) when (e is not OperationCanceledException)
            { throw new DocUpsertException("create", null, e); }
            return new UpsertResult(newId, UpsertMode.Created);
        }

        var docId = ids[0];
        try
        {
            // U2：保留 docID 更新正文
            if (!string.IsNullOrEmpty(title))
            {
                try { await siyuan.RenameDocByIdAsync(docId, title, ct); }
                catch (SiyuanOperationException) { /* 标题未变或幂等，非致命 */ }
            }
            var children = await siyuan.GetChildBlocksAsync(docId, ct);
            foreach (var blk in children)
                await siyuan.DeleteBlockAsync(blk.Id, ct);
            await siyuan.PrependBlockAsync(docId, bodyMd, ct);
            return new UpsertResult(docId, UpsertMode.Updated);
        }
        catch (Exception e) when (e is not OperationCanceledException and not SiyuanAuthException)
        {
            // U1 fallback：删旧重建
            try { await siyuan.RemoveDocByIdAsync(docId, ct); }
            catch (Exception rm) when (rm is not OperationCanceledException)
            { throw new DocUpsertException("remove-fallback", docId, e); }

            try
            {
                var rebuiltId = await siyuan.CreateDocWithMdAsync(notebookId, hpath, bodyMd, ct);
                return new UpsertResult(rebuiltId, UpsertMode.Rebuilt);
            }
            catch (Exception ce) when (ce is not OperationCanceledException)
            { throw new DocUpsertException("create-fallback", docId, ce); }
        }
    }
}
