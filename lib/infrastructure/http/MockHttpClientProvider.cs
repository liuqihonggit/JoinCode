namespace Infrastructure.Http;

/// <summary>
/// 模拟 HTTP 客户端提供者 — 拦截所有请求返回预设响应，0网络IO，调试/E2E测试用
/// 通过 JCC_HTTP_MODE=Mock 环境变量激活
/// </summary>
public sealed class MockHttpClientProvider : IHttpClientProvider
{
    private readonly MockHttpMessageHandler _handler = new();
    private readonly HttpClient _client;

    /// <summary>构造模拟 HTTP 客户端提供者,初始化基础地址为 http://mock.local</summary>
    public MockHttpClientProvider()
    {
        _client = new HttpClient(_handler) { BaseAddress = new Uri("http://mock.local") };
    }

    /// <inheritdoc/>
    public HttpClient GetClient() => _client;

    /// <inheritdoc/>
    public HttpClient GetClient(string name) => _client;

    /// <summary>
    /// 设置指定 URL 的模拟响应
    /// </summary>
    public void SetupResponse(Uri requestUri, HttpStatusCode statusCode, string content)
    {
        _handler.SetupResponse(requestUri, statusCode, content);
    }

    /// <summary>
    /// 设置任意请求的默认模拟响应
    /// </summary>
    public void SetupDefaultResponse(HttpStatusCode statusCode, string content)
    {
        _handler.SetupDefaultResponse(statusCode, content);
    }
}

/// <summary>
/// 模拟 HTTP 消息处理器 — 拦截 HTTP 请求并返回预设响应，0网络IO
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Content)> _responses = new();
    private (HttpStatusCode Status, string Content)? _defaultResponse;

    /// <summary>设置指定请求 URI 的模拟响应</summary>
    /// <param name="requestUri">请求 URI</param>
    /// <param name="statusCode">HTTP 状态码</param>
    /// <param name="content">响应内容</param>
    public void SetupResponse(Uri requestUri, HttpStatusCode statusCode, string content)
    {
        _responses[requestUri.AbsoluteUri] = (statusCode, content);
    }

    /// <summary>设置任意请求的默认模拟响应</summary>
    /// <param name="statusCode">HTTP 状态码</param>
    /// <param name="content">响应内容</param>
    public void SetupDefaultResponse(HttpStatusCode statusCode, string content)
    {
        _defaultResponse = (statusCode, content);
    }

    /// <summary>拦截请求并返回匹配的预设响应，无匹配时返回 501 Not Implemented</summary>
    /// <param name="request">HTTP 请求消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>预设的 HTTP 响应消息</returns>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_responses.TryGetValue(request.RequestUri?.AbsoluteUri ?? "", out var response))
        {
            return Task.FromResult(new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(response.Content),
                RequestMessage = request
            });
        }

        if (_defaultResponse is { } defaultResp)
        {
            return Task.FromResult(new HttpResponseMessage(defaultResp.Status)
            {
                Content = new StringContent(defaultResp.Content),
                RequestMessage = request
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotImplemented)
        {
            ReasonPhrase = "MockHttpClient: 未设置匹配的响应",
            RequestMessage = request
        });
    }
}
