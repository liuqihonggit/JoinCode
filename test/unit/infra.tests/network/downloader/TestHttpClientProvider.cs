namespace Infra.Services.Tests.Network.Downloader;

/// <summary>
/// 测试用 IHttpClientProvider — 包装固定 HttpClient,供 RangeDownloader 测试注入
/// </summary>
internal sealed class TestHttpClientProvider : IHttpClientProvider
{
    private readonly HttpClient _client;
    internal TestHttpClientProvider(HttpClient client) => _client = client;
    public HttpClient GetClient() => _client;
    public HttpClient GetClient(string name) => _client;
}
