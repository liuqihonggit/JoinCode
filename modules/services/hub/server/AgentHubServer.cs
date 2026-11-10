
namespace Core.Hub.Server;

public sealed partial class AgentHubServer : IAsyncDisposable {
    private readonly HttpListener _httpListener;
    private volatile SettingsJson _settings;
    private volatile IProviderDefinitionRegistry _providerRegistry;
    private readonly ILogger<AgentHubServer>? _logger;
    private readonly CancellationTokenSource _cts;
    private readonly AgentRegistry _agentRegistry = new();
    private volatile Dictionary<string, HttpClient> _providerClients = new();
    private Task? _listenerTask;
    private volatile bool _running;
    private int _disposed;

    /// <summary>获取服务器监听端口。</summary>
    public int Port { get; }

    /// <summary>获取服务器是否正在运行。</summary>
    public bool IsRunning => _running;

    /// <summary>获取已连接的 Agent 数量。</summary>
    public int ConnectedAgents => _agentRegistry.Count;

    /// <summary>
    /// 构造 AgentHubServer — 多 Agent 中央服务器。
    /// </summary>
    /// <param name="settings">settings.json 配置（vendor 字典作为模型配置来源）。</param>
    /// <param name="providerRegistry">Provider 定义注册表（用于配置认证头）。</param>
    /// <param name="port">监听端口，默认 3457。</param>
    /// <param name="host">监听主机，默认 localhost。</param>
    /// <param name="logger">日志记录器。</param>
    public AgentHubServer(
        SettingsJson settings,
        IProviderDefinitionRegistry providerRegistry,
        int port = 3457,
        string host = "localhost",
        ILogger<AgentHubServer>? logger = null) {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        _logger = logger;
        Port = port;
        _cts = new CancellationTokenSource();
        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add($"http://{host}:{port}/");
    }

    /// <summary>启动服务器。</summary>
    public void Start() {
        if (_running) {
            _logger?.LogWarning("[AgentHubServer] 服务器已在运行");
            return;
        }

        InitializeProviderClients();
        _httpListener.Start();
        _running = true;
        _logger?.LogInformation("[AgentHubServer] 服务器已启动，端口: {Port}", Port);

        _listenerTask = Task.Run(ListenAsync);
    }

    /// <summary>停止服务器。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task StopAsync(CancellationToken cancellationToken = default) {
        _cts.Cancel();
        _running = false;

        _httpListener.Stop();

        await _agentRegistry.CloseAllAsync(cancellationToken).ConfigureAwait(false);

        if (_listenerTask != null) {
            await _listenerTask.ConfigureAwait(false);
            _listenerTask = null;
        }

        foreach (var client in _providerClients.Values) {
            client.Dispose();
        }
        _providerClients.Clear();

        _logger?.LogInformation("[AgentHubServer] 服务器已停止");
    }

    private void InitializeProviderClients() {
        var newClients = new Dictionary<string, HttpClient>();

        foreach (var (providerName, profile) in _settings.Vendor) {
            if (string.IsNullOrEmpty(profile.Endpoint)) {
                _logger?.LogWarning("[AgentHubServer] Provider '{Name}' 无 endpoint，跳过", providerName);
                continue;
            }

            var handler = SocketsHttpHandlerFactory.CreateWithDnsRefresh();
            var endpoint = profile.Endpoint ?? throw new InvalidOperationException($"Provider '{providerName}' endpoint is null");
            var client = new HttpClient(handler) {
                Timeout = TimeSpan.FromMinutes(5),
                BaseAddress = new Uri(endpoint)
            };

            var definition = _providerRegistry.TryGet(providerName);
            var apiKey = ResolveApiKey(profile, definition);

            if (definition != null) {
                definition.ConfigureHttpClient(client, new ProviderConfig {
                    Vendor = providerName,
                    Protocol = profile.Protocol ?? ProtocolKindEnumConstants.OpenAiCompatible,
                    ApiKey = apiKey
                });
            } else {
                if (!string.IsNullOrEmpty(apiKey)) {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                }
            }

            newClients[providerName] = client;
            _logger?.LogInformation("[AgentHubServer] 已注册 Provider: {Name} ({Protocol})", providerName, profile.Protocol);
        }

        _providerClients = newClients;
    }

    /// <summary>
    /// 热重载 Provider 配置 — 全量读取 settings.json + 重建 ProviderRegistry + 重建 HttpClient 字典，原子替换。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    public async Task ReloadProvidersAsync(CancellationToken ct = default) {
        _logger?.LogInformation("[AgentHubServer] 开始热重载 Provider 配置");

        var fs = FileSystemFactory.Create();
        var newSettings = await ConfigLoader.LoadSettingsJsonAsync(fs, ct).ConfigureAwait(false);
        newSettings ??= new SettingsJson();

        var newRegistry = ProviderDefinitionRegistry.Create(
            new ModelConfigLoader(),
            fs);

        ApplyReload(newSettings, newRegistry);
    }

    /// <summary>
    /// 应用热重载 — 用新的 SettingsJson 和 ProviderDefinitionRegistry 原子替换 volatile 字段，重建 HttpClient 字典。
    /// </summary>
    /// <param name="newSettings">新的 SettingsJson。</param>
    /// <param name="newRegistry">新的 ProviderDefinitionRegistry。</param>
    internal void ApplyReload(SettingsJson newSettings, IProviderDefinitionRegistry newRegistry) {
        var oldClients = _providerClients;

        _settings = newSettings;
        _providerRegistry = newRegistry;
        InitializeProviderClients();

        foreach (var client in oldClients.Values) {
            client.Dispose();
        }

        _logger?.LogInformation("[AgentHubServer] 热重载完成 — Providers: {Count}", _providerClients.Count);
    }

    private static string ResolveApiKey(ProfileSettings profile, IProviderDefinition? definition) {
        if (definition != null) {
            var envKey = definition.ResolveApiKeyFromEnv();
            if (!string.IsNullOrEmpty(envKey)) return envKey;
        }

        if (!string.IsNullOrEmpty(profile.ApiKeyEnvVar)) {
            var envKey = Environment.GetEnvironmentVariable(profile.ApiKeyEnvVar);
            if (!string.IsNullOrEmpty(envKey)) return envKey;
        }

        return string.Empty;
    }

    private async Task ListenAsync() {
        while (!_cts.Token.IsCancellationRequested) {
            try {
                var context = await _httpListener.GetContextAsync().ConfigureAwait(false);
                try {
                    await HandleRequestAsync(context, _cts.Token).ConfigureAwait(false);
                } catch (Exception ex) {
                    _logger?.LogError(ex, "[AgentHubServer] HandleRequestAsync 异常");
                }
            } catch (HttpListenerException) when (_cts.Token.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                _logger?.LogError(ex, "[AgentHubServer] 处理请求失败");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken) {
        var path = context.Request.Url?.AbsolutePath ?? "/";

        switch (path) {
            case "/health":
                await WriteJsonResponseAsync(context.Response, new HubHealthResponse {
                    Status = "ok",
                    Agents = _agentRegistry.Count,
                    Providers = _providerClients.Count
                }).ConfigureAwait(false);
                break;

            case "/agents":
                await WriteJsonResponseAsync(context.Response, new HubAgentsResponse {
                    Agents = _agentRegistry.GetAll()
                }).ConfigureAwait(false);
                break;

            case "/v1/models":
                await HandleModelsAsync(context).ConfigureAwait(false);
                break;

            case "/v1/chat/completions":
                await HandleChatCompletionsAsync(context, cancellationToken).ConfigureAwait(false);
                break;

            case "/agent/register":
                await HandleAgentRegisterAsync(context, cancellationToken).ConfigureAwait(false);
                break;

            case "/agent/unregister":
                await HandleAgentUnregisterAsync(context, cancellationToken).ConfigureAwait(false);
                break;

            case "/agent/heartbeat":
                await HandleAgentHeartbeatAsync(context, cancellationToken).ConfigureAwait(false);
                break;

            case "/agent/message":
                await HandleAgentMessageAsync(context, cancellationToken).ConfigureAwait(false);
                break;

            case "/agent/route":
                await HandleAgentRouteAsync(context, cancellationToken).ConfigureAwait(false);
                break;

            default:
                if (path.StartsWith("/agent/messages/", StringComparison.Ordinal)) {
                    await HandleAgentMessagesSseAsync(context, path, cancellationToken).ConfigureAwait(false);
                } else {
                    context.Response.StatusCode = 404;
                    await WriteJsonResponseAsync(context.Response, new HubErrorResponse { Error = "Not found" }).ConfigureAwait(false);
                }
                break;
        }
    }

    private async Task HandleModelsAsync(HttpListenerContext context) {
        var models = new List<HubModelInfo>();

        foreach (var (providerName, profile) in _settings.Vendor) {
            if (profile.Models is not null) {
                foreach (var model in profile.Models) {
                    models.Add(new HubModelInfo {
                        Id = model.Id,
                        Provider = providerName,
                        OwnedBy = providerName
                    });
                }
            }

            if (!string.IsNullOrEmpty(profile.Model)) {
                models.Add(new HubModelInfo {
                    Id = profile.Model,
                    Provider = providerName,
                    OwnedBy = providerName
                });
            }
        }

        await WriteJsonResponseAsync(context.Response, new HubModelsResponse { Data = models }).ConfigureAwait(false);
    }

    private async Task HandleChatCompletionsAsync(HttpListenerContext context, CancellationToken cancellationToken) {
        var requestBody = await ReadRequestBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(requestBody)) {
            context.Response.StatusCode = 400;
            await WriteJsonResponseAsync(context.Response, new HubErrorResponse { Error = "Empty request body" }).ConfigureAwait(false);
            return;
        }

        var providerName = ResolveProvider(requestBody);
        if (string.IsNullOrEmpty(providerName) || !_providerClients.TryGetValue(providerName, out var providerClient)) {
            context.Response.StatusCode = 400;
            await WriteJsonResponseAsync(context.Response, new HubErrorResponse { Error = $"Provider '{providerName}' not found" }).ConfigureAwait(false);
            return;
        }

        var isStream = requestBody.Contains("\"stream\":true") || requestBody.Contains("\"stream\": true");

        try {
            using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions") { Content = content };

            var upstreamResponse = await providerClient.SendAsync(
                upstreamRequest,
                isStream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false);

            context.Response.StatusCode = (int)upstreamResponse.StatusCode;

            foreach (var header in upstreamResponse.Headers) {
                context.Response.Headers[header.Key] = string.Join(", ", header.Value);
            }

            if (isStream) {
                context.Response.ContentType = "text/event-stream";
                var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var upstreamReader = new StreamReader(upstreamStream);
                using var responseStream = context.Response.OutputStream;
                using var responseWriter = new StreamWriter(responseStream);

                string? line;
                while ((line = await upstreamReader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null) {
                    await responseWriter.WriteLineAsync(line).ConfigureAwait(false);
                    await responseWriter.FlushAsync().ConfigureAwait(false);
                }
            } else {
                context.Response.ContentType = "application/json";
                var responseBody = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var buffer = Encoding.UTF8.GetBytes(responseBody);
                await context.Response.OutputStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, "[AgentHubServer] 转发请求到 Provider '{Provider}' 失败", providerName);
            context.Response.StatusCode = 502;
            await WriteJsonResponseAsync(context.Response, new HubErrorResponse { Error = ex.Message }).ConfigureAwait(false);
        }
    }

    private string ResolveProvider(string requestBody) {
        var model = ExtractModel(requestBody);

        foreach (var (providerName, profile) in _settings.Vendor) {
            if (profile.Models != null && profile.Models.Any(m => m.Id == model)) {
                return providerName;
            }
        }

        if (!string.IsNullOrEmpty(model)) {
            foreach (var (providerName, profile) in _settings.Vendor) {
                if (!string.IsNullOrEmpty(profile.Model) && profile.Model == model) {
                    return providerName;
                }
            }
        }

        return _settings.Current?.Profile ?? string.Empty;
    }

    private static string ExtractModel(string requestBody) {
        var modelStart = requestBody.IndexOf("\"model\"", StringComparison.OrdinalIgnoreCase);
        if (modelStart < 0) return string.Empty;
        var colonIndex = requestBody.IndexOf(':', modelStart);
        if (colonIndex < 0) return string.Empty;
        var valueStart = requestBody.IndexOf('"', colonIndex + 1);
        if (valueStart < 0) return string.Empty;
        var valueEnd = requestBody.IndexOf('"', valueStart + 1);
        if (valueEnd < 0) return string.Empty;
        return requestBody.Substring(valueStart + 1, valueEnd - valueStart - 1);
    }

    private async Task HandleAgentRegisterAsync(HttpListenerContext context, CancellationToken cancellationToken) {
        var body = await ReadRequestBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var registerRequest = RelaxedJsonSerializer.Deserialize(body, HubServerJsonContext.Default.AgentRegisterRequest);

        if (registerRequest == null || string.IsNullOrEmpty(registerRequest.AgentId)) {
            context.Response.StatusCode = 400;
            await WriteJsonResponseAsync(context.Response, new HubErrorResponse { Error = "AgentId required" }).ConfigureAwait(false);
            return;
        }

        _agentRegistry.Register(registerRequest.AgentId, registerRequest.SessionId, registerRequest.Capabilities);
        _logger?.LogInformation("[AgentHubServer] Agent 注册: {AgentId}", registerRequest.AgentId);

        await WriteJsonResponseAsync(context.Response, new HubRegisterResponse { Status = "ok", AgentId = registerRequest.AgentId }).ConfigureAwait(false);
    }

    private async Task HandleAgentUnregisterAsync(HttpListenerContext context, CancellationToken cancellationToken) {
        var body = await ReadRequestBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var unregisterRequest = RelaxedJsonSerializer.Deserialize(body, HubServerJsonContext.Default.AgentUnregisterRequest);

        if (unregisterRequest == null || string.IsNullOrEmpty(unregisterRequest.AgentId)) {
            context.Response.StatusCode = 400;
            await WriteJsonResponseAsync(context.Response, new HubErrorResponse { Error = "AgentId required" }).ConfigureAwait(false);
            return;
        }

        _agentRegistry.Unregister(unregisterRequest.AgentId);
        _logger?.LogInformation("[AgentHubServer] Agent 注销: {AgentId}", unregisterRequest.AgentId);

        await WriteJsonResponseAsync(context.Response, new HubStatusResponse { Status = "ok" }).ConfigureAwait(false);
    }

    private async Task HandleAgentMessageAsync(HttpListenerContext context, CancellationToken cancellationToken) {
        var body = await ReadRequestBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var messageRequest = RelaxedJsonSerializer.Deserialize(body, HubServerJsonContext.Default.AgentMessageRequest);

        if (messageRequest == null) {
            context.Response.StatusCode = 400;
            await WriteJsonResponseAsync(context.Response, new HubErrorResponse { Error = "Invalid message format" }).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrEmpty(messageRequest.ToAgentId)) {
            var delivered = await _agentRegistry.DeliverMessageAsync(messageRequest.ToAgentId, messageRequest, cancellationToken).ConfigureAwait(false);
            await WriteJsonResponseAsync(context.Response, new HubStatusResponse { Status = delivered ? "delivered" : "agent_not_found" }).ConfigureAwait(false);
        } else {
            await _agentRegistry.BroadcastMessageAsync(messageRequest.FromAgentId, messageRequest, cancellationToken).ConfigureAwait(false);
            await WriteJsonResponseAsync(context.Response, new HubStatusResponse { Status = "broadcast" }).ConfigureAwait(false);
        }
    }

    private async Task HandleAgentRouteAsync(HttpListenerContext context, CancellationToken cancellationToken) {
        var body = await ReadRequestBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var routeRequest = RelaxedJsonSerializer.Deserialize(body, HubServerJsonContext.Default.AgentRouteRequest);

        if (routeRequest == null || string.IsNullOrEmpty(routeRequest.Capability)) {
            context.Response.StatusCode = 400;
            await WriteJsonResponseAsync(context.Response, new HubErrorResponse { Error = "Capability required" }).ConfigureAwait(false);
            return;
        }

        var message = new AgentMessageRequest {
            FromAgentId = routeRequest.FromAgentId,
            MessageType = routeRequest.MessageType,
            Content = routeRequest.Content,
            Timestamp = DateTimeOffset.UtcNow
        };

        var targetAgentId = await _agentRegistry.RouteByCapabilityAsync(routeRequest.Capability, message, cancellationToken).ConfigureAwait(false);

        await WriteJsonResponseAsync(context.Response, new HubRouteResponse {
            Status = targetAgentId is not null ? "delivered" : "no_agent",
            AgentId = targetAgentId
        }).ConfigureAwait(false);
    }

    private async Task HandleAgentHeartbeatAsync(HttpListenerContext context, CancellationToken cancellationToken) {
        var body = await ReadRequestBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var heartbeatRequest = RelaxedJsonSerializer.Deserialize(body, HubServerJsonContext.Default.AgentHeartbeatRequest);

        if (heartbeatRequest == null || string.IsNullOrEmpty(heartbeatRequest.AgentId)) {
            context.Response.StatusCode = 400;
            await WriteJsonResponseAsync(context.Response, new HubErrorResponse { Error = "AgentId required" }).ConfigureAwait(false);
            return;
        }

        var updated = _agentRegistry.UpdateHeartbeat(heartbeatRequest.AgentId);
        if (!updated) {
            context.Response.StatusCode = 404;
            await WriteJsonResponseAsync(context.Response, new HubErrorResponse { Error = "Agent not found" }).ConfigureAwait(false);
            return;
        }

        await WriteJsonResponseAsync(context.Response, new HubStatusResponse { Status = "ok" }).ConfigureAwait(false);
    }

    private async Task HandleAgentMessagesSseAsync(HttpListenerContext context, string path, CancellationToken cancellationToken) {
        var agentId = path["/agent/messages/".Length..];

        if (string.IsNullOrEmpty(agentId) || !_agentRegistry.IsRegistered(agentId)) {
            context.Response.StatusCode = 404;
            await WriteJsonResponseAsync(context.Response, new HubErrorResponse { Error = "Agent not found" }).ConfigureAwait(false);
            return;
        }

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["Connection"] = "keep-alive";

        try {
            await foreach (var message in _agentRegistry.ReceiveAsync(agentId, cancellationToken).ConfigureAwait(false)) {
                var json = JsonSerializer.Serialize(message, HubServerJsonContext.Default.AgentMessageRequest);
                var sseData = $"data: {json}\n\n";
                var buffer = Encoding.UTF8.GetBytes(sseData);
                await context.Response.OutputStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                await context.Response.OutputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) {
        } catch (HttpListenerException ex) {
            _logger?.LogDebug(ex, "[AgentHubServer] SSE 连接关闭");
        }

        try {
            context.Response.Close();
        } catch (ObjectDisposedException ex) {
            _logger?.LogDebug(ex, "[AgentHubServer] SSE Response 已关闭");
        }
    }

    private static async Task<string> ReadRequestBodyAsync(HttpListenerRequest request, CancellationToken cancellationToken) {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteJsonResponseAsync<T>(HttpListenerResponse response, T data) where T : class {
        var json = data switch {
            HubHealthResponse h => JsonSerializer.Serialize(h, HubServerJsonContext.Default.HubHealthResponse),
            HubAgentsResponse a => JsonSerializer.Serialize(a, HubServerJsonContext.Default.HubAgentsResponse),
            HubErrorResponse e => JsonSerializer.Serialize(e, HubServerJsonContext.Default.HubErrorResponse),
            HubStatusResponse s => JsonSerializer.Serialize(s, HubServerJsonContext.Default.HubStatusResponse),
            HubRegisterResponse r => JsonSerializer.Serialize(r, HubServerJsonContext.Default.HubRegisterResponse),
            HubModelsResponse m => JsonSerializer.Serialize(m, HubServerJsonContext.Default.HubModelsResponse),
            HubRouteResponse rt => JsonSerializer.Serialize(rt, HubServerJsonContext.Default.HubRouteResponse),
            _ => throw new InvalidOperationException($"Unsupported response type: {typeof(T).Name}")
        };
        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        response.Close();
    }

    /// <summary>异步释放服务器资源。</summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if (_running) {
            _cts.Cancel();
            _running = false;
            _httpListener.Stop();
        }

        if (_listenerTask != null) {
            try { await _listenerTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _listenerTask = null;
        }

        await _agentRegistry.CloseAllAsync(CancellationToken.None).ConfigureAwait(false);

        foreach (var client in _providerClients.Values) {
            client.Dispose();
        }
        _providerClients.Clear();

        _cts.Dispose();
    }
}
