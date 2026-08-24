
namespace Services.Api;

public sealed record ApiClientOptions
{
    public required string BaseUrl { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public RetryPolicyOptions? RetryOptions { get; init; }

    public Dictionary<string, string> DefaultHeaders { get; init; } = new();

    public string UserAgent { get; init; } = "JoinCode/1.0";

    public bool AutoSerializeJson { get; init; } = true;

    public IReadOnlyList<string>? FallbackEndpoints { get; init; }
}

[Register(typeof(IApiClient), ServiceLifetime.Singleton)]
public sealed partial class ApiClient : ServiceEntity, IApiClient, IDisposable
{
    private HttpClient _httpClient;
    private readonly RetryPolicy _retryPolicy;
    private readonly ApiClientOptions _options;
    private readonly ILogger<ApiClient>? _logger;
    private readonly INetworkConnectivityService? _networkService;
    private readonly IMtlsService? _mtlsService;
    private readonly IHttpProxyService? _httpProxyService;
    private Services.Api.Vcr.IVcrService? _vcrService;
    private VcrHttpHandler? _vcrHandler;
    private bool _disposed;

    public ApiClient(ApiClientOptions options, ILogger<ApiClient>? logger = null,
        IMtlsService? mtlsService = null, IHttpProxyService? httpProxyService = null,
        INetworkConnectivityService? networkService = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _networkService = networkService;
        _mtlsService = mtlsService;
        _httpProxyService = httpProxyService;

        _httpClient = BuildHttpClient();

        _retryPolicy = new RetryPolicy(options.RetryOptions ?? RetryPolicyOptions.Default);

        _logger?.LogInformation("[ApiClient] 初始化完成 - BaseUrl: {BaseUrl}, Timeout: {Timeout}s",
            options.BaseUrl, options.Timeout.TotalSeconds);

        if (_networkService is not null)
        {
            _networkService.StateChanged += OnNetworkStateChanged;
        }
    }

    /// <summary>
    /// DI 构造函数 — 从 IOptions&lt;ApiSettings&gt; 推导 ApiClientOptions，并设置 AuthToken
    /// </summary>
    public ApiClient(
        IOptions<ApiSettings>? settingsOptions = null,
        ILogger<ApiClient>? logger = null,
        IMtlsService? mtlsService = null,
        IHttpProxyService? httpProxyService = null,
        INetworkConnectivityService? networkService = null)
        : this(BuildOptions(settingsOptions), logger, mtlsService, httpProxyService, networkService)
    {
        var settings = settingsOptions?.Value;
        if (settings is not null && !string.IsNullOrEmpty(settings.AuthToken))
        {
            SetAuthorizationToken(settings.AuthToken, settings.AuthScheme);
        }
    }

    private static ApiClientOptions BuildOptions(IOptions<ApiSettings>? settingsOptions)
    {
        var settings = settingsOptions?.Value;
        return settings is not null && !string.IsNullOrEmpty(settings.BaseUrl)
            ? settings.ToApiClientOptions()
            : new ApiClientOptions { BaseUrl = "http://localhost:8080", Timeout = TimeSpan.FromSeconds(30) };
    }

    public void SetVcrService(Services.Api.Vcr.IVcrService vcrService)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(vcrService);

        _vcrService = vcrService;
        _vcrHandler = new VcrHttpHandler(vcrService, new VcrOptions(), _logger as ILogger<VcrHttpHandler>);
        _vcrHandler.InnerHandler = new HttpClientHandler();
        _httpClient = new HttpClient(_vcrHandler)
        {
            BaseAddress = _options.BaseUrl != null ? new Uri(_options.BaseUrl) : null,
            Timeout = _options.Timeout
        };

        _httpClient.DefaultRequestHeaders.Add("User-Agent", _options.UserAgent);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        foreach (var header in _options.DefaultHeaders)
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }

        _logger?.LogInformation("[ApiClient] VcrService 已集成，模式: {Mode}", vcrService.CurrentMode);
    }

    /// <summary>
    /// 构建 HttpClient — 提取自构造函数,供网络状态变化时重建 handler
    /// </summary>
    private HttpClient BuildHttpClient()
    {
        HttpMessageHandler? handler = null;

        if (_mtlsService != null && _mtlsService.IsMtlsConfigured)
        {
            var mtlsConfig = new MtlsConfiguration { IsConfigured = true };
            handler = _mtlsService.CreateMtlsHandler(mtlsConfig);
        }

        if (_httpProxyService != null && _httpProxyService.IsProxyConfigured)
        {
            var proxyHandler = _httpProxyService.CreateProxyHandler();
            if (handler is HttpClientHandler mtlsHandler)
            {
                var proxySettings = _httpProxyService.GetCurrentProxySettings();
                if (!string.IsNullOrEmpty(proxySettings.ProxyUrl))
                {
                    var proxyUri = new Uri(proxySettings.ProxyUrl);
                    var proxy = new WebProxy(proxyUri);
                    if (!string.IsNullOrEmpty(proxySettings.ProxyUsername))
                    {
                        proxy.Credentials = new System.Net.NetworkCredential(
                            proxySettings.ProxyUsername, proxySettings.ProxyPassword ?? string.Empty);
                    }
                    else if (proxySettings.UseDefaultCredentials)
                    {
                        proxy.Credentials = System.Net.CredentialCache.DefaultCredentials;
                    }
                    if (proxySettings.BypassHosts is { Count: > 0 })
                    {
                        proxy.BypassList = proxySettings.BypassHosts.ToArray();
                    }
                    mtlsHandler.Proxy = proxy;
                    mtlsHandler.UseProxy = true;
                }
            }
            else
            {
                handler = proxyHandler;
            }
        }

        var client = handler != null
            ? new HttpClient(handler) { BaseAddress = new Uri(_options.BaseUrl), Timeout = _options.Timeout }
            : new HttpClient { BaseAddress = new Uri(_options.BaseUrl), Timeout = _options.Timeout };

        client.DefaultRequestHeaders.Add("User-Agent", _options.UserAgent);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        foreach (var header in _options.DefaultHeaders)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }

        return client;
    }

    /// <summary>
    /// 网络状态变化时重建 HttpClient handler — VPN/代理切换后自动生效
    /// </summary>
    private void OnNetworkStateChanged(object? sender, NetworkConnectivityChangedEventArgs e)
    {
        if (e.PreviousState == e.CurrentState) return;
        try
        {
            Interlocked.Exchange(ref _httpClient, BuildHttpClient());
            _logger?.LogInformation("[ApiClient] 网络状态变化 {Prev}→{Curr},HttpClient 已重建", e.PreviousState, e.CurrentState);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ApiClient] 网络状态变化时重建 HttpClient 失败");
        }
    }

    /// <summary>
    /// 等待网络恢复 — 网络不可用时阻塞等待(带 30s 超时),恢复后继续;超时抛 ApiException
    /// </summary>
    private async Task WaitForNetworkAsync(CancellationToken ct)
    {
        if (_networkService is null) return;
        if (_networkService.IsNetworkAvailable()) return;

        _logger?.LogWarning("[ApiClient] 网络不可用,等待恢复...");

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<NetworkConnectivityChangedEventArgs> handler = (_, e) =>
        {
            if (e.CurrentState != NetworkConnectivityState.Offline) tcs.TrySetResult(true);
        };
        _networkService.StateChanged += handler;
        try
        {
            if (!_networkService.IsNetworkAvailable())
            {
                await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
            throw ApiException.Connection(_options.BaseUrl, new TimeoutException("等待网络恢复超时(30s)"));
        }
        finally
        {
            _networkService.StateChanged -= handler;
        }

        _logger?.LogInformation("[ApiClient] 网络已恢复");
    }

    public async Task<HttpResponseMessage> SendAsync(ApiRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        await WaitForNetworkAsync(cancellationToken).ConfigureAwait(false);

        var httpRequest = BuildHttpRequestMessage(request);
        var operation = async (CancellationToken ct) =>
        {
            var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return response;
        };

        if (request.SkipRetry)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (_options.FallbackEndpoints is { Count: > 0 } && ex is not OperationCanceledException)
        {
            return await TryFallbackEndpointsAsync(request, ex, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 尝试备用端点 — 主端点全部重试失败后,依次尝试备用端点
    /// </summary>
    private async Task<HttpResponseMessage> TryFallbackEndpointsAsync(ApiRequest request, Exception primaryException, CancellationToken ct)
    {
        foreach (var fallback in _options.FallbackEndpoints!)
        {
            try
            {
                _logger?.LogWarning("[ApiClient] 主端点失败,尝试备用端点: {Endpoint}", fallback);
                var client = _httpClient;
                var originalBase = client.BaseAddress;
                client.BaseAddress = new Uri(fallback);
                try
                {
                    var fallbackRequest = BuildHttpRequestMessage(request);
                    var response = await client.SendAsync(fallbackRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    _logger?.LogInformation("[ApiClient] 备用端点成功: {Endpoint}", fallback);
                    return response;
                }
                finally
                {
                    client.BaseAddress = originalBase;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[ApiClient] 备用端点失败: {Endpoint}", fallback);
            }
        }
        throw primaryException;
    }

    public async Task<ApiResponse<T>> RequestAsync<T>(ApiRequest request, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            return await ProcessResponseAsync(response, request.Path, jsonTypeInfo).ConfigureAwait(false);
        }
        catch (ApiException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "[ApiClient] 请求失败 - 路径: {Path}", request.Path);
            throw ApiException.Connection(request.Path, ex);
        }
    }

    public async Task<T> RequestOrThrowAsync<T>(ApiRequest request, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(request, jsonTypeInfo, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
            throw new ApiException(
                response.ErrorMessage ?? $"请求失败 (HTTP {response.StatusCode})",
                statusCode: response.StatusCode,
                endpoint: request.Path,
                responseContent: response.RawContent);
        }

        return response.Data ?? throw new InvalidOperationException("Response data is null.");
    }

    public Task<ApiResponse<T>> GetAsync<T>(string path, JsonTypeInfo<T> jsonTypeInfo, Dictionary<string, string>? queryParams = null, CancellationToken cancellationToken = default)
        => RequestAsync(ApiRequest.Get(path, queryParams), jsonTypeInfo, cancellationToken);

    public Task<ApiResponse<T>> PostAsync<T>(string path, string? body, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default)
        => RequestAsync(ApiRequest.Post(path, body), jsonTypeInfo, cancellationToken);

    public Task<ApiResponse<T>> PutAsync<T>(string path, string? body, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default)
        => RequestAsync(ApiRequest.Put(path, body), jsonTypeInfo, cancellationToken);

    public Task<ApiResponse<T>> PatchAsync<T>(string path, string? body, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default)
        => RequestAsync(ApiRequest.Patch(path, body), jsonTypeInfo, cancellationToken);

    public Task<ApiResponse<T>> DeleteAsync<T>(string path, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default)
        => RequestAsync(ApiRequest.Delete(path), jsonTypeInfo, cancellationToken);

    public void SetDefaultHeader(string name, string value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
    }

    public void RemoveDefaultHeader(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _httpClient.DefaultRequestHeaders.Remove(name);
    }

    public void SetAuthorizationToken(string token, string scheme = "Bearer")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(scheme, token);
    }

    public void ClearAuthorization()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    private HttpRequestMessage BuildHttpRequestMessage(ApiRequest request)
    {
        var uriBuilder = new UriBuilder(new Uri(_httpClient.BaseAddress ?? throw new InvalidOperationException("HttpClient.BaseAddress is not set."), request.Path));

        if (request.QueryParams?.Count > 0)
        {
            var query = string.Join("&", request.QueryParams.Select(p =>
                $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
            uriBuilder.Query = query;
        }

        var httpRequest = new HttpRequestMessage(request.Method, uriBuilder.Uri);

        if (request.Headers != null)
        {
            foreach (var header in request.Headers)
            {
                httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (request.Body != null)
        {
            httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        }

        return httpRequest;
    }

    private async Task<ApiResponse<T>> ProcessResponseAsync<T>(HttpResponseMessage response, string endpoint, JsonTypeInfo<T> jsonTypeInfo)
    {
        var headers = response.Headers.ToDictionary(
            h => h.Key,
            h => h.Value.ToList(),
            StringComparer.OrdinalIgnoreCase);

        var rawContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            T? data = default;
            if (!string.IsNullOrWhiteSpace(rawContent) && _options.AutoSerializeJson)
            {
                try
                {
                    data = JsonSerializer.Deserialize(rawContent, jsonTypeInfo);
                }
                catch (JsonException ex)
                {
                    _logger?.LogWarning(ex, "[ApiClient] JSON 反序列化失败 - 端点: {Endpoint}", endpoint);
                }
            }

            return ApiResponse<T>.SuccessResult(data ?? throw new InvalidOperationException("Deserialized data is null."), (int)response.StatusCode, headers, rawContent);
        }

        var exception = ClassifyError(response, endpoint, rawContent);
        throw exception;
    }

    private ApiException ClassifyError(HttpResponseMessage response, string endpoint, string rawContent)
    {
        var statusCode = (int)response.StatusCode;

        return statusCode switch
        {
            429 => CreateRateLimitException(response, endpoint, rawContent),
            >= 500 => new ServerErrorException(endpoint, statusCode, rawContent),
            401 or 403 => new AuthException(endpoint, statusCode, GetErrorMessage(rawContent, "认证失败", _logger), rawContent),
            400 or 422 => new ValidationException(endpoint, GetErrorMessage(rawContent, "请求参数无效", _logger), null, rawContent),
            _ => ApiException.ResponseError(endpoint, statusCode, rawContent)
        };
    }

    private RateLimitException CreateRateLimitException(HttpResponseMessage response, string endpoint, string rawContent)
    {
        TimeSpan? retryAfter = null;

        if (response.Headers.RetryAfter?.Delta.HasValue == true)
        {
            retryAfter = response.Headers.RetryAfter.Delta.Value;
        }
        else if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues) &&
                 resetValues.FirstOrDefault() is string resetValue &&
                 long.TryParse(resetValue, out var resetTimestamp))
        {
            var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetTimestamp);
            retryAfter = resetTime - DateTimeOffset.UtcNow;
        }

        return new RateLimitException(endpoint, retryAfter, rawContent);
    }

    private static string GetErrorMessage(string rawContent, string defaultMessage, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
            return defaultMessage;

        try
        {
            var node = JsonNode.Parse(rawContent);
            if (node?["error"] is JsonNode errorNode)
            {
                if (errorNode is JsonObject errorObj
                    && errorObj.TryGetPropertyValue("message", out var msgNode)
                    && msgNode is not null)
                {
                    return msgNode.GetValue<string>() ?? defaultMessage;
                }

                if (errorNode.GetValueKind() == JsonValueKind.String)
                    return errorNode.GetValue<string>() ?? defaultMessage;
            }

            if (node?["message"] is JsonNode directMsg)
                return directMsg.GetValue<string>() ?? defaultMessage;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "解析API错误响应失败，使用默认消息: {DefaultMessage}", defaultMessage);
        }

        return defaultMessage;
    }

    protected override void OnDispose()
    {
        if (!_disposed)
        {
            if (_networkService is not null)
            {
                _networkService.StateChanged -= OnNetworkStateChanged;
            }
            _httpClient.Dispose();
            _disposed = true;
        }
    }
}
