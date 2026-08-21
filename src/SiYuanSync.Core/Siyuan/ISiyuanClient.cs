using SiYuanSync.Core.Models;

namespace SiYuanSync.Core.Siyuan;

public interface ISiyuanClient
{
    Task<IReadOnlyList<Notebook>> ListNotebooksAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> GetDocIdsByHPathAsync(string notebookId, string hpath, CancellationToken ct);
    Task<string> CreateDocWithMdAsync(string notebookId, string hpath, string md, CancellationToken ct);
    Task RenameDocByIdAsync(string docId, string title, CancellationToken ct);
    Task RemoveDocByIdAsync(string docId, CancellationToken ct);
    Task<IReadOnlyList<BlockChild>> GetChildBlocksAsync(string docId, CancellationToken ct);
    Task DeleteBlockAsync(string blockId, CancellationToken ct);
    Task PrependBlockAsync(string parentDocId, string md, CancellationToken ct);
    Task SetDocSortModeAsync(string docId, int sortMode, CancellationToken ct);
}
