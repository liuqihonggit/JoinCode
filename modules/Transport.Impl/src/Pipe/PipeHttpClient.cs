namespace JoinCode.Transport;

/// <summary>
/// 基于命名管道的 HTTP 客户端
/// </summary>
public sealed class PipeHttpClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger? _logger;
    private bool _disposed;

    /// <summary>
    /// 创建管道 HTTP 客户端
    /// </summary>
    /// <param name="pipeName">管道名称</param>
    /// <param name="logger">日志记录器（可选）</param>
    public PipeHttpClient(string pipeName, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);

        _logger = logger;
        _httpClient = new HttpClient(new PipeHttpMessageHandler(pipeName, logger));

        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        _logger?.LogDebug("{Method} Created PipeHttpClient with pipe: {PipeName}", nameof(PipeHttpClient), pipeName);
    }

    /// <summary>
    /// 发送 HTTP 请求
    /// </summary>
    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        _logger?.LogDebug("{Method} Sending request to {Uri}", nameof(SendAsync), request.RequestUri);

        return _httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// 发送 POST 请求，内容为 JSON（AOT 安全，需传入 JsonTypeInfo）
    /// </summary>
    public Task<HttpResponseMessage> PostAsJsonAsync<TValue>(
        string requestUri,
        TValue value,
        JsonTypeInfo<TValue> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(requestUri);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        _logger?.LogDebug("{Method} POST {Uri} with JsonTypeInfo", nameof(PostAsJsonAsync), requestUri);

        return _httpClient.PostAsJsonAsync(requestUri, value, jsonTypeInfo, cancellationToken);
    }

    /// <summary>
    /// 发送 GET 请求
    /// </summary>
    public Task<HttpResponseMessage> GetAsync(
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(requestUri);

        _logger?.LogDebug("{Method} GET {Uri}", nameof(GetAsync), requestUri);

        return _httpClient.GetAsync(requestUri, cancellationToken);
    }

    /// <summary>
    /// 发送 GET 请求并解析 JSON 响应（AOT 安全，需传入 JsonTypeInfo）
    /// </summary>
    public async Task<T?> GetFromJsonAsync<T>(
        string requestUri,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(requestUri);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        _logger?.LogDebug("{Method} GET {Uri} with JsonTypeInfo", nameof(GetFromJsonAsync), requestUri);

        var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(jsonTypeInfo, cancellationToken);
    }

    /// <summary>
    /// 设置默认请求头
    /// </summary>
    public void SetDefaultHeader(string name, string? value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (value == null)
        {
            _httpClient.DefaultRequestHeaders.Remove(name);
        }
        else
        {
            _httpClient.DefaultRequestHeaders.Remove(name);
            _httpClient.DefaultRequestHeaders.Add(name, value);
        }

        _logger?.LogDebug("Set default header: {HeaderName} = {HeaderValue}", name, value ?? "(removed)");
    }

    /// <summary>
    /// 设置超时时间
    /// </summary>
    public void SetTimeout(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _httpClient.Timeout = timeout;
        _logger?.LogDebug("Set timeout to {Timeout}", timeout);
    }

    /// <summary>
    /// 设置基础地址
    /// </summary>
    public void SetBaseAddress(Uri baseAddress)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(baseAddress);

        _httpClient.BaseAddress = baseAddress;
        _logger?.LogDebug("Set base address to {BaseAddress}", baseAddress);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _logger?.LogDebug("{Method} Disposing PipeHttpClient", nameof(Dispose));

        _httpClient.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
