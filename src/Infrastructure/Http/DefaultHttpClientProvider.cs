namespace Infrastructure.Http;

/// <summary>
/// 默认 HTTP 客户端提供者 — 返回共享的 HttpClient 实例
/// </summary>
[Register(typeof(IHttpClientProvider))]
public sealed class DefaultHttpClientProvider : IHttpClientProvider
{
    private readonly HttpClient _sharedClient;

    public DefaultHttpClientProvider()
    {
        _sharedClient = new HttpClient();
    }

    public HttpClient GetClient() => _sharedClient;

    public HttpClient GetClient(string name) => _sharedClient;
}
