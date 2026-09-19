namespace Infrastructure.Http;

/// <summary>
/// 韧性 HTTP 客户端提供者 — 在 ResilientHttpExecutor 之上包装 IHttpClientProvider，提供熔断+重试+超时保护的 HTTP 请求发送
/// </summary>
public sealed class ResilientHttpClientProvider : IResilientHttpClientProvider {
    private readonly IHttpClientProvider _inner;
    private readonly ResiliencePolicy _policy;
    private readonly ILogger? _logger;
    private readonly ResilientHttpExecutor _executor;

    /// <summary>
    /// 构造韧性 HTTP 客户端提供者
    /// </summary>
    /// <param name="inner">底层 HTTP 客户端提供者</param>
    /// <param name="policy">韧性策略（可选，默认使用 HttpDefault）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="networkService">网络连通性服务（可选，用于网络中断时暂停重试预算）</param>
    public ResilientHttpClientProvider(
        IHttpClientProvider inner,
        ResiliencePolicy? policy = null,
        ILogger? logger = null,
        INetworkConnectivityService? networkService = null) {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _policy = policy ?? ResiliencePolicy.HttpDefault("default");
        _logger = logger;
        _executor = new ResilientHttpExecutor(_policy, logger, networkService);
    }

    /// <summary>底层韧性 HTTP 执行器 — 暴露熔断器状态供遥测采集</summary>
    public ResilientHttpExecutor Executor => _executor;

    /// <inheritdoc/>
    public HttpClient GetClient() {
        return _inner.GetClient();
    }

    /// <inheritdoc/>
    public HttpClient GetClient(string name) {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return _inner.GetClient(name);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> SendResilientAsync(
        HttpRequestMessage request,
        string operationName,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);

        return await _executor.ExecuteAsync(
            async token => {
                var clone = await CloneRequestAsync(request).ConfigureAwait(false);
                return await _inner.GetClient().SendAsync(clone, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            },
            operationName,
            ct).ConfigureAwait(false);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request) {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        if (request.Content is not null) {
            var contentBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            clone.Content = new ByteArrayContent(contentBytes);

            foreach (var header in request.Content.Headers) {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in request.Headers) {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        clone.Version = request.Version;

        return clone;
    }
}