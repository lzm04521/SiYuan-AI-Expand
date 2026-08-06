namespace SiYuanSync.Core.Siyuan;

internal sealed class LambdaHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _func;
    public LambdaHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> func) => _func = func;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _func(request, cancellationToken);
}
