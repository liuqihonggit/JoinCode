namespace JoinCode.Abstractions.Http;

/// <summary>
/// HTTP 客户端借用句柄 — 包装共享 HttpClient 实例,表达"借用"语义(调用方不 Dispose)
/// <para>由 IHttpClientProvider.GetClient() 返回,消除 JCC9305 误报(IDisposable 变量即拥有)</para>
/// <para>句柄不实现 IDisposable,调用方不应释放底层 HttpClient(其生命周期由提供者管理)</para>
/// <para>需要原始 HttpClient 的场景(如传给 BCL 方法)通过 Client 属性获取</para>
/// </summary>
public readonly struct HttpClientRef {
    private readonly HttpClient _client;

    /// <summary>构造借用句柄 — 包装指定 HttpClient</summary>
    /// <param name="client">被借用的 HttpClient 实例(不为 null)</param>
    public HttpClientRef(HttpClient client) {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>获取原始 HttpClient — 逃生舱,供需要 HttpClient 的 BCL/第三方 API 场景使用</summary>
    /// <remarks>调用方通过此属性取得的 HttpClient 仍不拥有其生命周期,不应 Dispose</remarks>
    public HttpClient Client => _client;

    /// <inheritdoc cref="HttpClient.SendAsync(HttpRequestMessage)"/>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        => _client.SendAsync(request);

    /// <inheritdoc cref="HttpClient.SendAsync(HttpRequestMessage, CancellationToken)"/>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _client.SendAsync(request, cancellationToken);

    /// <inheritdoc cref="HttpClient.SendAsync(HttpRequestMessage, HttpCompletionOption)"/>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption)
        => _client.SendAsync(request, completionOption);

    /// <inheritdoc cref="HttpClient.SendAsync(HttpRequestMessage, HttpCompletionOption, CancellationToken)"/>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => _client.SendAsync(request, completionOption, cancellationToken);

    /// <inheritdoc cref="HttpClient.GetAsync(string)"/>
    public Task<HttpResponseMessage> GetAsync(string requestUri)
        => _client.GetAsync(requestUri);

    /// <inheritdoc cref="HttpClient.GetAsync(string, CancellationToken)"/>
    public Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken cancellationToken)
        => _client.GetAsync(requestUri, cancellationToken);

    /// <inheritdoc cref="HttpClient.GetAsync(Uri)"/>
    public Task<HttpResponseMessage> GetAsync(Uri requestUri)
        => _client.GetAsync(requestUri);

    /// <inheritdoc cref="HttpClient.GetAsync(Uri, CancellationToken)"/>
    public Task<HttpResponseMessage> GetAsync(Uri requestUri, CancellationToken cancellationToken)
        => _client.GetAsync(requestUri, cancellationToken);

    /// <inheritdoc cref="HttpClient.GetAsync(string, HttpCompletionOption, CancellationToken)"/>
    public Task<HttpResponseMessage> GetAsync(string requestUri, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => _client.GetAsync(requestUri, completionOption, cancellationToken);

    /// <inheritdoc cref="HttpClient.PostAsync(string, HttpContent)"/>
    public Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content)
        => _client.PostAsync(requestUri, content);

    /// <inheritdoc cref="HttpClient.PostAsync(string, HttpContent, CancellationToken)"/>
    public Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content, CancellationToken cancellationToken)
        => _client.PostAsync(requestUri, content, cancellationToken);

    /// <inheritdoc cref="HttpClient.PostAsync(Uri, HttpContent)"/>
    public Task<HttpResponseMessage> PostAsync(Uri requestUri, HttpContent content)
        => _client.PostAsync(requestUri, content);

    /// <inheritdoc cref="HttpClient.PostAsync(Uri, HttpContent, CancellationToken)"/>
    public Task<HttpResponseMessage> PostAsync(Uri requestUri, HttpContent content, CancellationToken cancellationToken)
        => _client.PostAsync(requestUri, content, cancellationToken);

    /// <inheritdoc cref="HttpClient.PutAsync(string, HttpContent)"/>
    public Task<HttpResponseMessage> PutAsync(string requestUri, HttpContent content)
        => _client.PutAsync(requestUri, content);

    /// <inheritdoc cref="HttpClient.PutAsync(string, HttpContent, CancellationToken)"/>
    public Task<HttpResponseMessage> PutAsync(string requestUri, HttpContent content, CancellationToken cancellationToken)
        => _client.PutAsync(requestUri, content, cancellationToken);

    /// <inheritdoc cref="HttpClient.DeleteAsync(string)"/>
    public Task<HttpResponseMessage> DeleteAsync(string requestUri)
        => _client.DeleteAsync(requestUri);

    /// <inheritdoc cref="HttpClient.DeleteAsync(string, CancellationToken)"/>
    public Task<HttpResponseMessage> DeleteAsync(string requestUri, CancellationToken cancellationToken)
        => _client.DeleteAsync(requestUri, cancellationToken);

    /// <inheritdoc cref="HttpClient.PatchAsync(string, HttpContent)"/>
    public Task<HttpResponseMessage> PatchAsync(string requestUri, HttpContent content)
        => _client.PatchAsync(requestUri, content);

    /// <inheritdoc cref="HttpClient.PatchAsync(string, HttpContent, CancellationToken)"/>
    public Task<HttpResponseMessage> PatchAsync(string requestUri, HttpContent content, CancellationToken cancellationToken)
        => _client.PatchAsync(requestUri, content, cancellationToken);

    /// <inheritdoc cref="HttpClient.GetByteArrayAsync(string, CancellationToken)"/>
    public Task<byte[]> GetByteArrayAsync(string requestUri, CancellationToken cancellationToken)
        => _client.GetByteArrayAsync(requestUri, cancellationToken);

    /// <inheritdoc cref="HttpClient.GetByteArrayAsync(string)"/>
    public Task<byte[]> GetByteArrayAsync(string requestUri)
        => _client.GetByteArrayAsync(requestUri);

    /// <inheritdoc cref="HttpClient.GetStringAsync(string, CancellationToken)"/>
    public Task<string> GetStringAsync(string requestUri, CancellationToken cancellationToken)
        => _client.GetStringAsync(requestUri, cancellationToken);

    /// <inheritdoc cref="HttpClient.GetStringAsync(string)"/>
    public Task<string> GetStringAsync(string requestUri)
        => _client.GetStringAsync(requestUri);

    /// <inheritdoc cref="HttpClient.GetStreamAsync(string, CancellationToken)"/>
    public Task<Stream> GetStreamAsync(string requestUri, CancellationToken cancellationToken)
        => _client.GetStreamAsync(requestUri, cancellationToken);

    /// <inheritdoc cref="HttpClient.GetStreamAsync(string)"/>
    public Task<Stream> GetStreamAsync(string requestUri)
        => _client.GetStreamAsync(requestUri);

    /// <summary>请求超时 — 转发 HttpClient.Timeout</summary>
    public TimeSpan Timeout {
        get => _client.Timeout;
        set => _client.Timeout = value;
    }

    /// <summary>基础地址 — 转发 HttpClient.BaseAddress</summary>
    public Uri? BaseAddress {
        get => _client.BaseAddress;
        set => _client.BaseAddress = value;
    }

    /// <summary>默认请求头 — 转发 HttpClient.DefaultRequestHeaders</summary>
    public System.Net.Http.Headers.HttpRequestHeaders DefaultRequestHeaders => _client.DefaultRequestHeaders;

    /// <summary>默认响应头 — 转发 HttpClient.DefaultRequestVersion</summary>
    public Version DefaultRequestVersion {
        get => _client.DefaultRequestVersion;
        set => _client.DefaultRequestVersion = value;
    }
}
