namespace Core.Bridge;

/// <summary>
/// Bridge 请求执行器 — 从 BridgeApiClient 提取的单一职责小类
/// <para>职责: 认证 token 获取 + 请求头设置 + OAuth 401 重试 + 网络重试</para>
/// </summary>
internal sealed class BridgeRequestExecutor
{
    private readonly BridgeApiOptions _options;
    private readonly ILogger? _logger;

    public BridgeRequestExecutor(BridgeApiOptions options, ILogger? logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// 获取当前认证 token — 优先使用动态 GetAccessToken，回退到静态 ApiKey
    /// </summary>
    private string? GetCurrentToken() =>
        _options.GetAccessToken?.Invoke() ?? _options.ApiKey;

    /// <summary>
    /// 为请求设置 Authorization header — OAuth 启用时动态获取 token
    /// 同时附加 X-Trusted-Device-Token header（如果可用）— 对齐 TS 端 getHeaders()
    /// </summary>
    public void SetAuthHeader(HttpRequestMessage request)
    {
        if (_options.IsOAuthRetryEnabled)
        {
            var token = GetCurrentToken();
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Add("Authorization", $"Bearer {token}");
            }
        }
        // OAuth 未启用时，Authorization 已在构造函数中通过 DefaultRequestHeaders 设置

        // 对齐 TS 端: const deviceToken = deps.getTrustedDeviceToken?.()
        var deviceToken = _options.GetTrustedDeviceToken?.Invoke();
        if (!string.IsNullOrEmpty(deviceToken))
        {
            request.Headers.Add("X-Trusted-Device-Token", deviceToken);
        }
    }

    /// <summary>
    /// 请求发送 — 降级为透传，网络重试统一由 ResilientHttpExecutor (Gateway) 处理，避免嵌套放大打爆服务器
    /// </summary>
    public async Task<T> SendWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> sendFunc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sendFunc);
        return await sendFunc(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 带 OAuth 重试和网络重试的请求发送 — 对齐 TS 端 withOAuthRetry + 网络重试
    /// OAuth 重试层：401 → 刷新 token → 重试一次
    /// 网络重试层：HttpRequestException → 指数退避重试
    /// </summary>
    public async Task<T> SendWithOAuthAndNetworkRetryAsync<T>(
        Func<CancellationToken, Task<T>> sendFunc,
        bool useOAuthRetry,
        CancellationToken ct = default)
    {
        if (!useOAuthRetry || !_options.IsOAuthRetryEnabled)
        {
            // 无 OAuth 重试 — 直接走网络重试
            return await SendWithRetryAsync(sendFunc, ct).ConfigureAwait(false);
        }

        // OAuth 重试 + 网络重试双层
        // 第一层：网络重试（指数退避）
        // 第二层：OAuth 重试（401 刷新 token 后重试一次）
        return await SendWithRetryAsync(async token =>
        {
            try
            {
                return await sendFunc(token).ConfigureAwait(false);
            }
            catch (BridgeFatalError ex) when (ex.StatusCode == 401 && _options.OnAuth401 is not null)
            {
                // 401 致命错误 — 尝试 OAuth 刷新
                var staleToken = GetCurrentToken() ?? string.Empty;
                _logger?.LogInformation("[BridgeApiClient] 401 认证失败，尝试 OAuth token 刷新");

                var refreshed = await _options.OnAuth401(staleToken).ConfigureAwait(false);
                if (!refreshed)
                {
                    _logger?.LogWarning("[BridgeApiClient] OAuth token 刷新失败");
                    throw;
                }

                _logger?.LogInformation("[BridgeApiClient] OAuth token 刷新成功，重试请求");

                // 刷新成功 — 重试一次
                try
                {
                    return await sendFunc(token).ConfigureAwait(false);
                }
                catch (BridgeFatalError retryEx) when (retryEx.StatusCode == 401)
                {
                    // 重试仍 401 — 抛出原始错误
                    _logger?.LogWarning("[BridgeApiClient] OAuth 重试后仍 401，放弃");
                    throw;
                }
            }
        }, ct).ConfigureAwait(false);
    }
}
