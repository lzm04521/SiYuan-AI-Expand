using System.Net;
using System.Text;
using System.Text.Json;
using SiYuanSync.Core.Models;

namespace SiYuanSync.Core.Siyuan;

public sealed class SiyuanClient : ISiyuanClient
{
    private static readonly HashSet<string> AuthHints = new(StringComparer.OrdinalIgnoreCase) { "auth", "token", "unauthorized", "forbidden", "permission" };
    private readonly HttpClient _http;

    public SiyuanClient(HttpMessageHandler handler, SiyuanConnectionConfig conn)
    {
        _http = new HttpClient(handler) { BaseAddress = new Uri(conn.ServerUrl.TrimEnd('/')), Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.Authorization = new("Token", conn.Token);
    }

    public Task<IReadOnlyList<Notebook>> ListNotebooksAsync(CancellationToken ct) =>
        SendAndReadAsync<IReadOnlyList<Notebook>>(SiyuanEndpoints.ListNotebooks, new { }, root =>
            root.GetProperty("data").GetProperty("notebooks").EnumerateArray().Select(Notebook.FromJson).ToList(), ct);

    public Task<IReadOnlyList<string>> GetDocIdsByHPathAsync(string notebookId, string hpath, CancellationToken ct) =>
        SendAndReadAsync(SiyuanEndpoints.GetIdsByHPath, new { notebook = notebookId, path = hpath }, root =>
            (IReadOnlyList<string>)root.GetProperty("data").EnumerateArray().Select(e => e.GetString()!).ToList(), ct);

    public Task<string> CreateDocWithMdAsync(string notebookId, string hpath, string md, CancellationToken ct) =>
        SendAndReadAsync(SiyuanEndpoints.CreateDocWithMd, new { notebook = notebookId, path = hpath, markdown = md },
            root => root.GetProperty("data").GetString()!, ct);

    public Task RenameDocByIdAsync(string docId, string title, CancellationToken ct) =>
        SendAsync(SiyuanEndpoints.RenameDocById, new { id = docId, title }, ct);

    public Task RemoveDocByIdAsync(string docId, CancellationToken ct) =>
        SendAsync(SiyuanEndpoints.RemoveDocById, new { id = docId }, ct);

    public Task<IReadOnlyList<BlockChild>> GetChildBlocksAsync(string docId, CancellationToken ct) =>
        SendAndReadAsync(SiyuanEndpoints.GetChildBlocks, new { id = docId }, root =>
            (IReadOnlyList<BlockChild>)root.GetProperty("data").EnumerateArray().Select(BlockChild.FromJson).ToList(), ct);

    public Task DeleteBlockAsync(string blockId, CancellationToken ct) =>
        SendAsync(SiyuanEndpoints.DeleteBlock, new { id = blockId }, ct);

    public Task PrependBlockAsync(string parentDocId, string md, CancellationToken ct) =>
        SendAsync(SiyuanEndpoints.PrependBlock, new { parentID = parentDocId, dataType = "markdown", data = md }, ct);

    public Task SetDocSortModeAsync(string docId, int sortMode, CancellationToken ct) =>
        SendAsync(SiyuanEndpoints.SetDocSortMode, new { id = docId, sortMode }, ct);

    private async Task<T> SendAndReadAsync<T>(string endpoint, object body, Func<JsonElement, T> map, CancellationToken ct)
    {
        using var doc = await ReadEnvelopeAsync(endpoint, body, ct);
        return map(doc.RootElement);
    }

    private async Task SendAsync(string endpoint, object body, CancellationToken ct)
    {
        using var doc = await ReadEnvelopeAsync(endpoint, body, ct);
    }

    private async Task<JsonDocument> ReadEnvelopeAsync(string endpoint, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        { throw new SiyuanTransientException("请求超时", ex); }
        catch (HttpRequestException ex)
        { throw new SiyuanTransientException("网络错误：" + ex.Message, ex); }

        if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
            throw new SiyuanAuthException($"HTTP {(int)resp.StatusCode}");

        string text = await resp.Content.ReadAsStringAsync(ct);
        if ((int)resp.StatusCode >= 500)
            throw new SiyuanTransientException($"HTTP {(int)resp.StatusCode}");

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        int code = root.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
        string msg = root.TryGetProperty("msg", out var m) ? m.GetString() ?? "" : "";
        if (code != 0)
        {
            if ((int)resp.StatusCode is 401 or 403 || AuthHints.Any(h => msg.Contains(h, StringComparison.OrdinalIgnoreCase)))
                throw new SiyuanAuthException($"思源鉴权失败：{msg}");
            throw new SiyuanOperationException($"思源返回错误 code={code}：{msg}");
        }
        // 验证通过，重新解析返回给调用方（调用方负责 Dispose）。
        return JsonDocument.Parse(text);
    }
}
