using System.Net;
using System.Text.Json;
using SiYuanSync.Core.Models;
using SiYuanSync.Core.Siyuan;
using Xunit;

namespace SiYuanSync.Core.Tests;

public class SiyuanClientEndpointsTests
{
    private static (SiyuanClient client, List<HttpRequestMessage> sent) Make(Func<HttpRequestMessage, HttpResponseMessage> resp)
    {
        var sent = new List<HttpRequestMessage>();
        var handler = new LambdaHandler(async (req, ct) =>
        {
            sent.Add(Clone(req));
            return resp(req);
        });
        var client = new SiyuanClient(handler, new SiyuanConnectionConfig("http://sy:6806", "tok"));
        return (client, sent);
    }

    private static HttpRequestMessage Clone(HttpRequestMessage r)
    {
        var clone = new HttpRequestMessage(r.Method, r.RequestUri);
        foreach (var h in r.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        return clone;
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new() { StatusCode = code, Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ListNotebooks_parses_notebooks_and_sends_token()
    {
        var (client, sent) = Make(_ => Json("""{"code":0,"msg":"","data":{"notebooks":[{"id":"n1","name":"AI","closed":false}]}}"""));
        var nbs = await client.ListNotebooksAsync(default);
        Assert.Single(nbs);
        Assert.Equal("AI", nbs[0].Name);
        Assert.Equal("Token tok", sent[0].Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task GetDocIdsByHPath_returns_empty_when_absent()
    {
        var (client, _) = Make(_ => Json("""{"code":0,"msg":"","data":[]}"""));
        var ids = await client.GetDocIdsByHPathAsync("n1", "/JPT", default);
        Assert.Empty(ids);
    }

    [Fact]
    public async Task CreateDocWithMd_returns_new_docId()
    {
        var (client, _) = Make(_ => Json("""{"code":0,"msg":"","data":"20240101-doc"}"""));
        var id = await client.CreateDocWithMdAsync("n1", "/JPT/x", "# x", default);
        Assert.Equal("20240101-doc", id);
    }

    [Fact]
    public async Task Nonzero_code_throws_operation_exception()
    {
        var (client, _) = Make(_ => Json("""{"code":-1,"msg":"boom","data":null}"""));
        await Assert.ThrowsAsync<SiyuanOperationException>(() => client.ListNotebooksAsync(default));
    }

    [Fact]
    public async Task Http_401_throws_auth_exception()
    {
        var (client, _) = Make(_ => Json("""{"code":0,"msg":"","data":null}""", HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<SiyuanAuthException>(() => client.ListNotebooksAsync(default));
    }

    [Fact]
    public async Task Siyuan_auth_error_code_throws_auth_exception()
    {
        // 思源 鉴权失败通常返回 code != 0 且 msg 含 "auth"/"token"/"Unauthorized"
        var (client, _) = Make(_ => Json("""{"code":401,"msg":"Unauthorized","data":null}"""));
        await Assert.ThrowsAsync<SiyuanAuthException>(() => client.ListNotebooksAsync(default));
    }
}
