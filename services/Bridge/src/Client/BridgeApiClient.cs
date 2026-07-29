
namespace Core.Bridge;

using JoinCode.Abstractions.Attributes;

#region CCR 数据模型 — 对齐 TS 端 bridgeApi.ts / types.ts

/// <summary>
/// Bridge 环境注册请求 — 对齐 TS 端 POST /v1/environments/bridge
/// </summary>
public sealed partial class BridgeEnvironmentRegistration
{
    [JsonPropertyName("bridge_id")]
    public required string BridgeId { get; init; }

    [JsonPropertyName("machine_name")]
    public string? MachineName { get; init; }

    [JsonPropertyName("dir")]
    public string? Dir { get; init; }

    [JsonPropertyName("branch")]
    public string? Branch { get; init; }

    [JsonPropertyName("git_repo_url")]
    public string? GitRepoUrl { get; init; }

    [JsonPropertyName("max_sessions")]
    public int MaxSessions { get; init; }

    [JsonPropertyName("spawn_mode")]
    public string? SpawnMode { get; init; }

    [JsonPropertyName("worker_type")]
    public string? WorkerType { get; init; }

    /// <summary>
    /// 重用已有环境 ID — 对齐 TS 端 reuseEnvironmentId
    /// 重连时传递，让服务器尝试复活同一环境
    /// </summary>
    [JsonPropertyName("reuse_environment_id")]
    public string? ReuseEnvironmentId { get; init; }
}

/// <summary>
/// Bridge 环境注册响应 — 对齐 TS 端 POST /v1/environments/bridge 响应
/// </summary>
public sealed partial class BridgeEnvironmentRegistrationResponse
{
    [JsonPropertyName("environment_id")]
    public required string EnvironmentId { get; init; }

    [JsonPropertyName("bridge_id")]
    public required string BridgeId { get; init; }

    [JsonPropertyName("session_ingress_url")]
    public string? SessionIngressUrl { get; init; }
}

/// <summary>
/// 工作轮询项 — 对齐 TS 端 GET .../work/poll 响应
/// </summary>
public sealed partial class BridgeWorkItem
{
    [JsonPropertyName("work_id")]
    public required string WorkId { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("sdk_url")]
    public string? SdkUrl { get; init; }

    [JsonPropertyName("session_ingress_token")]
    public string? SessionIngressToken { get; init; }

    [JsonPropertyName("api_base_url")]
    public string? ApiBaseUrl { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("work_type")]
    public string? WorkType { get; init; }

    /// <summary>
    /// base64url 编码的工作密钥 — 对齐 TS 端 WorkResponse.secret
    /// 解码后包含 session_ingress_token、api_base_url、use_code_sessions 等
    /// </summary>
    [JsonPropertyName("secret")]
    public string? Secret { get; init; }
}

/// <summary>
/// 权限响应事件 — 对齐 TS 端 POST /v1/sessions/{id}/events
/// </summary>
public sealed partial class BridgePermissionResponseEvent
{
    [JsonPropertyName("event_type")]
    public required string EventType { get; init; }

    [JsonPropertyName("behavior")]
    public required string Behavior { get; init; }

    [JsonPropertyName("updated_input")]
    public string? UpdatedInput { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// 会话重连请求 — 对齐 TS 端 POST .../bridge/reconnect
/// </summary>
public sealed partial class BridgeReconnectRequest
{
    [JsonPropertyName("environment_id")]
    public required string EnvironmentId { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }
}

/// <summary>
/// 会话重连响应 — 对齐 TS 端 POST .../bridge/reconnect 响应
/// </summary>
public sealed partial class BridgeReconnectResponse
{
    [JsonPropertyName("sdk_url")]
    public string? SdkUrl { get; init; }

    [JsonPropertyName("session_ingress_token")]
    public string? SessionIngressToken { get; init; }

    [JsonPropertyName("api_base_url")]
    public string? ApiBaseUrl { get; init; }
}

/// <summary>
/// 心跳响应 — 对齐 TS 端 POST .../work/{id}/heartbeat 响应
/// </summary>
public sealed partial class BridgeHeartbeatResponse
{
    [JsonPropertyName("acknowledged")]
    public required bool Acknowledged { get; init; }

    [JsonPropertyName("lease_extended_until")]
    public string? LeaseExtendedUntil { get; init; }
}

#endregion

/// <summary>
/// Bridge 致命错误 — 对齐 TS 端 BridgeFatalError
/// 不可重试的错误（如认证失败、环境不存在）
/// </summary>
public sealed partial class BridgeFatalError : Exception
{
    public int? StatusCode { get; init; }
    public string? ErrorType { get; init; }

    public BridgeFatalError(string message, int? statusCode = null, string? errorType = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
    }
}

/// <summary>
/// Bridge API 选项 - 远程 API 通信配置
/// </summary>
[Register]
public sealed partial class BridgeApiOptions
{
    /// <summary>默认重试次数</summary>
    public const int DefaultMaxRetries = 3;

    /// <summary>默认重试基础延迟（毫秒）</summary>
    public const int DefaultRetryBaseDelayMs = 1000;

    /// <summary>默认 API 超时（秒）</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>默认 OAuth 重试超时（毫秒）</summary>
    public const int DefaultOAuthRetryTimeoutMs = 30_000;

    /// <summary>API 基础 URL</summary>
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; init; } = "http://localhost:3456";

    /// <summary>API 密钥</summary>
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; init; }

    /// <summary>请求超时</summary>
    [JsonPropertyName("timeout")]
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(DefaultTimeoutSeconds);

    /// <summary>最大重试次数</summary>
    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; init; } = DefaultMaxRetries;

    /// <summary>重试基础延迟</summary>
    [JsonPropertyName("retryBaseDelayMs")]
    public int RetryBaseDelayMs { get; init; } = DefaultRetryBaseDelayMs;

    /// <summary>
    /// 获取当前访问令牌 — 对齐 TS 端 resolveAuth()
    /// 提供后，需要 OAuth 重试的 API 方法将使用动态 token
    /// 未提供时，使用构造时固定的 ApiKey
    /// </summary>
    public Func<string?>? GetAccessToken { get; init; }

    /// <summary>
    /// 401 认证失败回调 — 对齐 TS 端 deps.onAuth401
    /// 刷新 token，返回 true 表示成功
    /// </summary>
    public Func<string, Task<bool>>? OnAuth401 { get; init; }

    /// <summary>是否启用 OAuth 重试（GetAccessToken 和 OnAuth401 均提供时启用）</summary>
    [JsonIgnore]
    public bool IsOAuthRetryEnabled => GetAccessToken is not null && OnAuth401 is not null;

    /// <summary>
    /// 组织 UUID — 对齐 TS 端 x-organization-uuid header
    /// Bridge API 需要此 header 进行组织级路由
    /// </summary>
    [JsonPropertyName("orgUUID")]
    public string? OrgUUID { get; init; }

    /// <summary>
    /// 获取受信设备令牌 — 对齐 TS 端 deps.getTrustedDeviceToken
    /// v1 路径: 每次请求时延迟调用，非空则附加 X-Trusted-Device-Token header
    /// </summary>
    [JsonIgnore]
    public Func<string?>? GetTrustedDeviceToken { get; init; }

    public BridgeApiOptions() { }

    public BridgeApiOptions(BridgeConfig config)
    {
        BaseUrl = string.IsNullOrEmpty(config.ApiBaseUrl) ? "http://localhost:3456" : config.ApiBaseUrl;
        ApiKey = config.ApiKey;
        Timeout = TimeSpan.FromSeconds(config.ApiTimeoutSeconds);
    }
}

/// <summary>
/// Bridge API 客户端 - 远程 Bridge API 通信客户端
/// 使用 JsonSerializer + JsonContext 实现 AOT 兼容的 JSON 序列化
/// </summary>
[Register]
public sealed partial class BridgeApiClient : IDisposable
{
    /// <summary>对齐 TS 端 anthropic-beta header — 与 BridgeSessionApi.BetaHeader 一致</summary>
    private const string BetaHeader = "ccr-byoc-2025-07-29";

    private readonly HttpClient _httpClient;
    private readonly BridgeApiOptions _options;
    [Inject] private readonly ILogger<BridgeApiClient>? _logger;
    private int _isDisposed;

    /// <summary>
    /// 暴露内部 HttpClient 供 RegisterWorkerAsync 等需要独立 HTTP 调用的场景使用
    /// </summary>
    internal HttpClient HttpClient => _httpClient;

    /// <summary>
    /// 外部 HttpClient 构造函数 — 用于测试和 BridgeMainCommand/BridgeRemoteCore 等需要自定义 HttpClient 的场景
    /// </summary>
    public BridgeApiClient(
        HttpClient httpClient,
        BridgeApiOptions options,
        ILogger<BridgeApiClient>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        InitializeHttpClient();
    }

    /// <summary>
    /// DI 构造函数 — 从 BridgeConfig 自动创建 HttpClient 和 BridgeApiOptions
    /// 这是 DI 容器唯一可见的 public 构造函数（另一个改为 internal 以避免歧义）
    /// </summary>
    public BridgeApiClient(
        BridgeConfig? config = null,
        BridgeApiOptions? options = null,
        ILogger<BridgeApiClient>? logger = null)
    {
        _options = options ?? new BridgeApiOptions(config ?? new BridgeConfig());
        _httpClient = new HttpClient { Timeout = _options.Timeout };
        _logger = logger;
        InitializeHttpClient();
    }

    private void InitializeHttpClient()
    {
        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.Timeout = _options.Timeout;

        // OAuth 启用时不设置静态 Authorization header — 每次请求动态获取 token
        // 未启用时保持旧行为：构造时固定 ApiKey
        if (!_options.IsOAuthRetryEnabled && !string.IsNullOrEmpty(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");
        }
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
    private void SetAuthHeader(HttpRequestMessage request)
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
    /// 发送 GET 请求
    /// </summary>
    /// <typeparam name="T">响应类型</typeparam>
    /// <param name="path">请求路径</param>
    /// <param name="jsonTypeInfo">AOT 兼容的 JSON 类型信息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>反序列化的响应</returns>
    public async Task<T?> GetAsync<T>(
        string path,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        var response = await _httpClient.GetAsync(path, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, jsonTypeInfo);
    }

    /// <summary>
    /// 发送 POST 请求
    /// </summary>
    /// <typeparam name="TRequest">请求体类型</typeparam>
    /// <typeparam name="TResponse">响应类型</typeparam>
    /// <param name="path">请求路径</param>
    /// <param name="body">请求体</param>
    /// <param name="requestTypeInfo">请求体 JSON 类型信息</param>
    /// <param name="responseTypeInfo">响应 JSON 类型信息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>反序列化的响应</returns>
    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        var json = JsonSerializer.Serialize(body, requestTypeInfo);
        using var content = new StringContent(json, Encoding.UTF8, HttpContentType.ApplicationJson.ToValue());

        var response = await _httpClient.PostAsync(path, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(responseJson, responseTypeInfo);
    }

    /// <summary>
    /// 发送 PUT 请求
    /// </summary>
    /// <typeparam name="TRequest">请求体类型</typeparam>
    /// <typeparam name="TResponse">响应类型</typeparam>
    /// <param name="path">请求路径</param>
    /// <param name="body">请求体</param>
    /// <param name="requestTypeInfo">请求体 JSON 类型信息</param>
    /// <param name="responseTypeInfo">响应 JSON 类型信息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>反序列化的响应</returns>
    public async Task<TResponse?> PutAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        var json = JsonSerializer.Serialize(body, requestTypeInfo);
        using var content = new StringContent(json, Encoding.UTF8, HttpContentType.ApplicationJson.ToValue());

        var response = await _httpClient.PutAsync(path, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(responseJson, responseTypeInfo);
    }

    /// <summary>
    /// 发送 DELETE 请求
    /// </summary>
    /// <param name="path">请求路径</param>
    /// <param name="ct">取消令牌</param>
    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        var response = await _httpClient.DeleteAsync(path, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// 带重试的请求发送 - 指数退避重试逻辑
    /// </summary>
    /// <typeparam name="T">响应类型</typeparam>
    /// <param name="sendFunc">实际发送函数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>响应结果</returns>
    public async Task<T> SendWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> sendFunc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sendFunc);

        var maxRetries = _options.MaxRetries;
        var baseDelayMs = _options.RetryBaseDelayMs;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await sendFunc(ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                var delayMs = (int)Math.Min(baseDelayMs * Math.Pow(2, attempt), WorkflowConstants.Retry.MaxDelayMs);
                var jitter = Random.Shared.Next(0, (int)(delayMs * 0.1));
                var totalDelay = delayMs + jitter;

                _logger?.LogWarning(
                    "[BridgeApiClient] 请求失败（第 {Attempt}/{MaxRetries} 次），{DelayMs}ms 后重试: {Error}",
                    attempt + 1, maxRetries, totalDelay, ex.Message);

                await Task.Delay(totalDelay, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 带 OAuth 重试和网络重试的请求发送 — 对齐 TS 端 withOAuthRetry + 网络重试
    /// OAuth 重试层：401 → 刷新 token → 重试一次
    /// 网络重试层：HttpRequestException → 指数退避重试
    /// </summary>
    /// <typeparam name="T">响应类型</typeparam>
    /// <param name="sendFunc">实际发送函数（接受 CancellationToken）</param>
    /// <param name="useOAuthRetry">是否使用 OAuth 重试（仅使用 accessToken 的方法启用）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>响应结果</returns>
    private async Task<T> SendWithOAuthAndNetworkRetryAsync<T>(
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

    /// <summary>
    /// 健康检查 - 检查 API 是否可用
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否健康</returns>
    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/health", ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[BridgeApiClient] 健康检查失败");
            return false;
        }
    }

    /// <summary>
    /// 获取会话列表
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>会话 ID 列表</returns>
    public async Task<IReadOnlyList<string>> GetSessionsAsync(CancellationToken ct = default)
    {
        return await SendWithRetryAsync(async token =>
        {
            var response = await _httpClient.GetAsync("/sessions", token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeClientsData);
            return (IReadOnlyList<string>)(data?.Clients ?? new List<string>());
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 创建会话
    /// </summary>
    /// <param name="clientInfo">客户端信息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>连接数据</returns>
    public async Task<BridgeConnectedData?> CreateSessionAsync(
        ClientInfo clientInfo,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(clientInfo);

        return await SendWithRetryAsync(async token =>
        {
            var json = JsonSerializer.Serialize(clientInfo, BridgeJsonContext.Default.ClientInfo);
            using var content = new StringContent(json, Encoding.UTF8, HttpContentType.ApplicationJson.ToValue());

            var response = await _httpClient.PostAsync("/sessions", content, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            return JsonSerializer.Deserialize(responseJson, BridgeJsonContext.Default.BridgeConnectedData);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 关闭会话
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="ct">取消令牌</param>
    public async Task CloseSessionAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        await SendWithRetryAsync(async token =>
        {
            var response = await _httpClient.DeleteAsync($"/sessions/{sessionId}", token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return true;
        }, ct).ConfigureAwait(false);
    }

    #region CCR 专用 API 方法 — 对齐 TS 端 bridgeApi.ts

    /// <summary>
    /// 注册 Bridge 环境 — 对齐 TS 端 registerBridgeEnvironment
    /// POST /v1/environments/bridge
    /// TS 端使用 withOAuthRetry（accessToken 认证）
    /// </summary>
    public async Task<BridgeEnvironmentRegistrationResponse?> RegisterBridgeEnvironmentAsync(
        BridgeEnvironmentRegistration registration,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return await SendWithOAuthAndNetworkRetryAsync(async token =>
        {
            var json = JsonSerializer.Serialize(registration, BridgeJsonContext.Default.BridgeEnvironmentRegistration);
            using var content = new StringContent(json, Encoding.UTF8, HttpContentType.ApplicationJson.ToValue());

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/environments/bridge") { Content = content };
            SetAuthHeader(request);

            var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new BridgeFatalError("Authentication failed", (int)response.StatusCode, "auth_error");
            }

            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            return JsonSerializer.Deserialize(responseJson, BridgeJsonContext.Default.BridgeEnvironmentRegistrationResponse);
        }, useOAuthRetry: true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 轮询工作 — 对齐 TS 端 pollForWork
    /// GET /v1/environments/bridge/{environmentId}/work/poll
    /// 长轮询：服务器在有工作可用时才返回，否则保持连接直到超时
    /// </summary>
    public async Task<BridgeWorkItem?> PollForWorkAsync(
        string environmentId,
        CancellationToken ct = default,
        int? reclaimOlderThanMs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);

        if (!ValidateBridgeId(environmentId))
        {
            throw new ArgumentException("bridgeId must match pattern: ^[a-zA-Z0-9_-]+$");
        }

        try
        {
            // 对齐 TS 端: reclaim_older_than_ms 作为 URL 查询参数
            var url = $"/v1/environments/bridge/{environmentId}/work/poll";
            if (reclaimOlderThanMs.HasValue)
            {
                url += $"?reclaim_older_than_ms={reclaimOlderThanMs.Value}";
            }

            var response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            // 对齐 TS 端: validateStatus: s < 500 — 4xx 由 HandleErrorStatus 处理，5xx 重试
            if ((int)response.StatusCode >= 500)
            {
                response.EnsureSuccessStatusCode();
            }

            // 对齐 TS 端: handleErrorStatus — 401/403/404/410/429 分层处理
            if ((int)response.StatusCode is >= 400 and < 500)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                HandleErrorStatus((int)response.StatusCode, body, "Poll");
            }

            // 204 No Content 表示没有可用工作
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeWorkItem);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 正常取消，返回 null
            return null;
        }
    }

    /// <summary>
    /// 确认工作 — 对齐 TS 端 acknowledgeWork
    /// POST /v1/environments/bridge/{environmentId}/work/{workId}/ack
    /// TS 端使用 session_ingress_token 作为 Bearer 认证
    /// </summary>
    public async Task AcknowledgeWorkAsync(
        string environmentId,
        string workId,
        string? sessionToken = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workId);

        if (!ValidateBridgeId(environmentId) || !ValidateBridgeId(workId))
        {
            throw new ArgumentException("bridgeId and workId must match pattern: ^[a-zA-Z0-9_-]+$");
        }

        await SendWithRetryAsync(async token =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"/v1/environments/bridge/{environmentId}/work/{workId}/ack");

            // 对齐 TS 端: sessionToken 作为 Authorization Bearer header
            if (!string.IsNullOrEmpty(sessionToken))
            {
                request.Headers.Add("Authorization", $"Bearer {sessionToken}");
            }

            var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return true;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 停止工作 — 对齐 TS 端 stopWork
    /// POST /v1/environments/bridge/{environmentId}/work/{workId}/stop
    /// TS 端使用 withOAuthRetry（accessToken 认证）
    /// </summary>
    public async Task StopWorkAsync(
        string environmentId,
        string workId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workId);

        if (!ValidateBridgeId(environmentId) || !ValidateBridgeId(workId))
        {
            throw new ArgumentException("bridgeId and workId must match pattern: ^[a-zA-Z0-9_-]+$");
        }

        await SendWithOAuthAndNetworkRetryAsync(async token =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"/v1/environments/bridge/{environmentId}/work/{workId}/stop");
            SetAuthHeader(request);

            var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return true;
        }, useOAuthRetry: true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 注销 Bridge 环境 — 对齐 TS 端 deregisterEnvironment
    /// DELETE /v1/environments/bridge/{environmentId}
    /// TS 端使用 withOAuthRetry（accessToken 认证）
    /// </summary>
    public async Task DeregisterEnvironmentAsync(
        string environmentId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);

        if (!ValidateBridgeId(environmentId))
        {
            throw new ArgumentException("bridgeId must match pattern: ^[a-zA-Z0-9_-]+$");
        }

        await SendWithOAuthAndNetworkRetryAsync(async token =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete,
                $"/v1/environments/bridge/{environmentId}");
            SetAuthHeader(request);

            var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return true;
        }, useOAuthRetry: true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 归档会话 — 对齐 TS 端 archiveSession
    /// POST /v1/sessions/{sessionId}/archive
    /// TS 端使用 withOAuthRetry（accessToken 认证）
    /// 对齐 TS 端 headers: anthropic-version + anthropic-beta + x-organization-uuid
    /// </summary>
    public async Task ArchiveSessionAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!ValidateBridgeId(sessionId))
        {
            throw new ArgumentException("sessionId must match pattern: ^[a-zA-Z0-9_-]+$");
        }

        await SendWithOAuthAndNetworkRetryAsync(async token =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"/v1/sessions/{sessionId}/archive");
            SetAuthHeader(request);
            // 对齐 TS 端 BridgeSessionApi.ArchiveAsync headers
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Add("anthropic-beta", BetaHeader);
            if (_options.OrgUUID is not null)
                request.Headers.Add("x-organization-uuid", _options.OrgUUID);

            var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return true;
        }, useOAuthRetry: true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 重连会话 — 对齐 TS 端 reconnectSession
    /// POST /v1/environments/bridge/{environmentId}/sessions/{sessionId}/bridge/reconnect
    /// TS 端使用 withOAuthRetry（accessToken 认证）
    /// </summary>
    public async Task<BridgeReconnectResponse?> ReconnectSessionAsync(
        string environmentId,
        string sessionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!ValidateBridgeId(environmentId) || !ValidateBridgeId(sessionId))
        {
            throw new ArgumentException("environmentId and sessionId must match pattern: ^[a-zA-Z0-9_-]+$");
        }

        return await SendWithOAuthAndNetworkRetryAsync(async token =>
        {
            var request = new BridgeReconnectRequest
            {
                EnvironmentId = environmentId,
                SessionId = sessionId
            };

            var json = JsonSerializer.Serialize(request, BridgeJsonContext.Default.BridgeReconnectRequest);
            using var content = new StringContent(json, Encoding.UTF8, HttpContentType.ApplicationJson.ToValue());

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post,
                $"/v1/environments/bridge/{environmentId}/sessions/{sessionId}/bridge/reconnect")
            { Content = content };
            SetAuthHeader(httpRequest);

            var response = await _httpClient.SendAsync(httpRequest, token).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new BridgeFatalError("Authentication failed during reconnect", (int)response.StatusCode, "auth_error");
            }

            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            return JsonSerializer.Deserialize(responseJson, BridgeJsonContext.Default.BridgeReconnectResponse);
        }, useOAuthRetry: true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 工作心跳 — 对齐 TS 端 heartbeatWork
    /// POST /v1/environments/bridge/{environmentId}/work/{workId}/heartbeat
    /// 延长工作租约，防止超时
    /// TS 端使用 session_ingress_token 作为 Bearer 认证
    /// </summary>
    public async Task<BridgeHeartbeatResponse?> HeartbeatWorkAsync(
        string environmentId,
        string workId,
        string? sessionToken = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workId);

        if (!ValidateBridgeId(environmentId) || !ValidateBridgeId(workId))
        {
            throw new ArgumentException("bridgeId and workId must match pattern: ^[a-zA-Z0-9_-]+$");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"/v1/environments/bridge/{environmentId}/work/{workId}/heartbeat");

            // 对齐 TS 端: sessionToken 作为 Authorization Bearer header
            if (!string.IsNullOrEmpty(sessionToken))
            {
                request.Headers.Add("Authorization", $"Bearer {sessionToken}");
            }

            var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new BridgeFatalError("Authentication failed during heartbeat", (int)response.StatusCode, "auth_error");
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeHeartbeatResponse);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// 发送权限响应事件 — 对齐 TS 端 sendPermissionResponseEvent
    /// POST /v1/sessions/{sessionId}/events
    /// </summary>
    public async Task SendPermissionResponseEventAsync(
        string sessionId,
        BridgePermissionResponseEvent permissionEvent,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(permissionEvent);

        if (!ValidateBridgeId(sessionId))
        {
            throw new ArgumentException("sessionId must match pattern: ^[a-zA-Z0-9_-]+$");
        }

        await SendWithRetryAsync(async token =>
        {
            var json = JsonSerializer.Serialize(permissionEvent, BridgeJsonContext.Default.BridgePermissionResponseEvent);
            using var content = new StringContent(json, Encoding.UTF8, HttpContentType.ApplicationJson.ToValue());

            var response = await _httpClient.PostAsync(
                $"/v1/sessions/{sessionId}/events",
                content, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return true;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 验证 Bridge ID 格式 — 对齐 TS 端 validateBridgeId
    /// 防止路径遍历攻击
    /// </summary>
    public static bool ValidateBridgeId(string bridgeId)
    {
        if (string.IsNullOrWhiteSpace(bridgeId))
        {
            return false;
        }

        // 只允许字母数字、连字符、下划线
        foreach (var c in bridgeId)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 获取会话标题 — 对齐 TS 端 fetchSessionTitle → getBridgeSession
    /// GET /v1/sessions/{sessionId} → 提取 title 字段
    /// </summary>
    public async Task<string?> GetSessionTitleAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!ValidateBridgeId(sessionId))
        {
            return null;
        }

        try
        {
            return await SendWithOAuthAndNetworkRetryAsync(async token =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"/v1/sessions/{sessionId}");
                SetAuthHeader(request);
                request.Headers.Add("anthropic-version", "2023-06-01");
                request.Headers.Add("anthropic-beta", BetaHeader);
                if (_options.OrgUUID is not null)
                    request.Headers.Add("x-organization-uuid", _options.OrgUUID);

                var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                var data = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.DictionaryStringJsonElement);
                if (data is not null && data.TryGetValue("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String)
                {
                    return titleEl.GetString();
                }

                return null;
            }, useOAuthRetry: true, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取会话详情 — 对齐 TS 端 getBridgeSession
    /// GET /v1/sessions/{sessionId} → 返回 environment_id 等字段
    /// 用于 resume 流程获取 reuseEnvironmentId
    /// </summary>
    public async Task<string?> GetBridgeSessionEnvironmentIdAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!ValidateBridgeId(sessionId))
        {
            return null;
        }

        try
        {
            return await SendWithOAuthAndNetworkRetryAsync(async token =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"/v1/sessions/{sessionId}");
                SetAuthHeader(request);
                request.Headers.Add("anthropic-version", "2023-06-01");
                request.Headers.Add("anthropic-beta", BetaHeader);
                if (_options.OrgUUID is not null)
                    request.Headers.Add("x-organization-uuid", _options.OrgUUID);

                var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                var data = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.DictionaryStringJsonElement);
                if (data is not null && data.TryGetValue("environment_id", out var envIdEl) && envIdEl.ValueKind == JsonValueKind.String)
                {
                    return envIdEl.GetString();
                }

                return null;
            }, useOAuthRetry: true, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 更新会话标题 — 对齐 TS 端 updateBridgeSessionTitle
    /// PATCH /v1/sessions/{sessionId} body={title}
    /// 错误静默处理（best-effort）
    /// </summary>
    public async Task UpdateSessionTitleAsync(
        string sessionId,
        string title,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (string.IsNullOrWhiteSpace(title)) return;

        if (!ValidateBridgeId(sessionId))
        {
            return;
        }

        try
        {
            await SendWithOAuthAndNetworkRetryAsync(async token =>
            {
                var body = new Dictionary<string, string> { ["title"] = title };
                var json = JsonSerializer.Serialize(body, BridgeJsonContext.Default.DictionaryStringString);
                using var content = new StringContent(json, Encoding.UTF8, HttpContentType.ApplicationJson.ToValue());

                using var request = new HttpRequestMessage(HttpMethod.Patch,
                    $"/v1/sessions/{sessionId}")
                { Content = content };
                SetAuthHeader(request);
                request.Headers.Add("anthropic-version", "2023-06-01");
                request.Headers.Add("anthropic-beta", BetaHeader);
                if (_options.OrgUUID is not null)
                    request.Headers.Add("x-organization-uuid", _options.OrgUUID);

                var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);

                // 对齐 TS 端: validateStatus: s < 500 — 4xx 静默，5xx 重试
                if ((int)response.StatusCode >= 500)
                {
                    response.EnsureSuccessStatusCode();
                }

                return true;
            }, useOAuthRetry: true, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 对齐 TS 端: 错误静默吞掉 — 标题同步是 best-effort
            System.Diagnostics.Trace.WriteLine($"[BridgeApiClient] Sync title failed: {ex.Message}");
        }
    }

    #endregion

    #region 错误处理辅助方法 — 对齐 TS 端 bridgeApi.ts handleErrorStatus/extractErrorType/extractErrorDetail

    /// <summary>
    /// 处理非 2xx 响应状态 — 对齐 TS 端 handleErrorStatus
    /// 从响应体提取 errorType 和 detail，按状态码抛出 BridgeFatalError 或 Exception
    /// </summary>
    internal static void HandleErrorStatus(int status, string? responseBody, string context)
    {
        if (status is 200 or 204) return;

        var detail = ExtractErrorDetail(responseBody);
        var errorType = ExtractErrorTypeFromData(responseBody);

        switch (status)
        {
            case 401:
                throw new BridgeFatalError(
                    $"{context}: Authentication failed (401){(detail is not null ? $": {detail}" : "")}. Please run `claude remote-control` to authenticate.",
                    status, errorType);
            case 403:
                throw new BridgeFatalError(
                    IsExpiredErrorType(errorType)
                        ? "Remote Control session has expired. Please restart with `claude remote-control` or /remote-control."
                        : $"{context}: Access denied (403){(detail is not null ? $": {detail}" : "")}. Check your organization permissions.",
                    status, errorType);
            case 404:
                throw new BridgeFatalError(
                    detail ?? $"{context}: Not found (404). Remote Control may not be available for this organization.",
                    status, errorType);
            case 410:
                throw new BridgeFatalError(
                    detail ?? "Remote Control session has expired. Please restart with `claude remote-control` or /remote-control.",
                    status, errorType ?? "environment_expired");
            case 429:
                throw new InvalidOperationException($"{context}: Rate limited (429). Polling too frequently.");
            default:
                throw new InvalidOperationException(
                    $"{context}: Failed with status {status}{(detail is not null ? $": {detail}" : "")}");
        }
    }

    /// <summary>
    /// 从响应体 JSON 提取 errorType — 对齐 TS 端 extractErrorTypeFromData
    /// 路径: data.error.type
    /// </summary>
    internal static string? ExtractErrorTypeFromData(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return null;
        try
        {
            var data = JsonSerializer.Deserialize(responseBody, BridgeJsonContext.Default.DictionaryStringJsonElement);
            if (data is not null &&
                data.TryGetValue("error", out var errorEl) &&
                errorEl.ValueKind == JsonValueKind.Object)
            {
                // error 是对象，尝试提取 error.type
                var errorDict = JsonSerializer.Deserialize(errorEl.GetRawText(), BridgeJsonContext.Default.DictionaryStringJsonElement);
                if (errorDict is not null &&
                    errorDict.TryGetValue("type", out var typeEl) &&
                    typeEl.ValueKind == JsonValueKind.String)
                {
                    return typeEl.GetString();
                }
            }
        }
        catch (Exception ex) { /* 解析失败返回 null */ System.Diagnostics.Trace.WriteLine($"[BridgeApiClient] Extract error type failed: {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// 从响应体 JSON 提取错误详情 — 对齐 TS 端 extractErrorDetail
    /// 优先 data.message，其次 data.error.message
    /// </summary>
    internal static string? ExtractErrorDetail(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return null;
        try
        {
            var data = JsonSerializer.Deserialize(responseBody, BridgeJsonContext.Default.DictionaryStringJsonElement);
            if (data is null) return null;

            // 优先 data.message
            if (data.TryGetValue("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
            {
                return msgEl.GetString();
            }

            // 其次 data.error.message
            if (data.TryGetValue("error", out var errorEl) && errorEl.ValueKind == JsonValueKind.Object)
            {
                var errorDict = JsonSerializer.Deserialize(errorEl.GetRawText(), BridgeJsonContext.Default.DictionaryStringJsonElement);
                if (errorDict is not null &&
                    errorDict.TryGetValue("message", out var errMsgEl) &&
                    errMsgEl.ValueKind == JsonValueKind.String)
                {
                    return errMsgEl.GetString();
                }
            }
        }
        catch (Exception ex) { /* 解析失败返回 null */ System.Diagnostics.Trace.WriteLine($"[BridgeApiClient] Extract error detail failed: {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// 判断 errorType 是否为过期类型 — 对齐 TS 端 isExpiredErrorType
    /// </summary>
    public static bool IsExpiredErrorType(string? errorType)
    {
        if (string.IsNullOrEmpty(errorType)) return false;
        return errorType.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
               errorType.Contains("lifetime", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断 403 是否可抑制 — 对齐 TS 端 isSuppressible403
    /// 某些 403 是"装饰性"的（缺少非核心 scope），不应以错误形式打扰用户
    /// </summary>
    public static bool IsSuppressible403(BridgeFatalError err)
    {
        if (err.StatusCode != 403) return false;
        return err.Message.Contains("external_poll_sessions", StringComparison.OrdinalIgnoreCase) ||
               err.Message.Contains("environments:manage", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 描述 HTTP 错误 — 对齐 TS 端 describeAxiosError
    /// 从 HttpResponseMessage 提取基础消息 + 服务器返回的详细信息
    /// </summary>
    public static string DescribeHttpError(Exception ex)
    {
        var msg = ex.Message;
        if (ex is HttpRequestException httpEx && httpEx.Data.Contains("ResponseBody"))
        {
            var body = httpEx.Data["ResponseBody"] as string;
            var detail = ExtractErrorDetail(body);
            if (detail is not null)
            {
                return $"{msg}: {detail}";
            }
        }
        return msg;
    }

    #endregion

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        _httpClient.Dispose();
    }
}
