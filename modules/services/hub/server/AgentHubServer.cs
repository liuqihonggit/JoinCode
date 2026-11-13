namespace Core.Hub.Server;

public sealed partial class AgentHubServer : IAsyncDisposable {
    private readonly HttpListener _httpListener;
    private readonly ILogger<AgentHubServer>? _logger;
    private readonly CancellationTokenSource _cts;
    private readonly AgentRegistry _agentRegistry = new();
    private volatile ProviderCatalog _catalog = new();
    private Task? _listenerTask;
    private Task? _tcpListenerTask;
    private Task? _startupStatusTask;
    private Task? _modelFetchTask;
    private readonly TcpListener? _tcpListener;
    private volatile bool _running;
    private int _disposed;

    /// <summary>TCP 消息推送端口（SSE 降级备用）。</summary>
    public int TcpPort { get; }

    /// <summary>获取服务器监听端口。</summary>
    public int Port { get; }

    /// <summary>获取服务器是否正在运行。</summary>
    public bool IsRunning => _running;

    /// <summary>获取已连接的 Agent 数量。</summary>
    public int ConnectedAgents => _agentRegistry.Count;

    /// <summary>获取供应商目录 — 统一内存结构，消费点通过 catalog[providerName].CurrentModel 解引用。</summary>
    public ProviderCatalog Catalog => _catalog;

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
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(providerRegistry);
        _logger = logger;
        Port = port;
        TcpPort = port + 1;
        _cts = new CancellationTokenSource();
        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add($"http://{host}:{port}/");
        _tcpListener = new TcpListener(IPAddress.Loopback, TcpPort);

        BuildCatalog(settings, providerRegistry);
    }

    /// <summary>启动服务器。</summary>
    public void Start() {
        if (_running) {
            _logger?.LogWarning("[AgentHubServer] 服务器已在运行");
            return;
        }

        _httpListener.Start();
        _running = true;
        _logger?.LogInformation("[AgentHubServer] 服务器已启动，端口: {Port}", Port);

        _startupStatusTask = Task.Run(async () => await ReportStartupStatusAsync().ConfigureAwait(false));
        _modelFetchTask = Task.Run(async () => await FetchModelsAsync().ConfigureAwait(false));

        _listenerTask = Task.Run(ListenAsync);
        if (_tcpListener is not null) {
            _tcpListener.Start();
            _tcpListenerTask = Task.Run(TcpListenAsync);
            _logger?.LogInformation("[AgentHubServer] TCP 消息端口已启动: {TcpPort}", TcpPort);
        }
    }

    /// <summary>
    /// 从 settings + registry 构建 ProviderCatalog — 把协议/模型/APIKey/Endpoint 组装到 ProviderEntry 中。
    /// </summary>
    private void BuildCatalog(SettingsJson settings, IProviderDefinitionRegistry registry) {
        var currentProvider = settings.Current?.Profile ?? string.Empty;
        var entries = new Dictionary<string, ProviderEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var (providerName, profile) in settings.Vendor) {
            if (string.IsNullOrEmpty(profile.Endpoint)) {
                _logger?.LogWarning("[AgentHubServer] Provider '{Name}' 无 endpoint，跳过", providerName);
                continue;
            }

            var definition = registry.TryGet(providerName);
            var apiKey = ResolveApiKey(profile, definition);
            var models = BuildModelEntries(profile);
            var currentModel = ResolveCurrentModel(profile, models);

            var entry = new ProviderEntry(
                name: providerName,
                endpoint: profile.Endpoint ?? string.Empty,
                protocol: profile.Protocol ?? ProtocolKindEnumConstants.OpenAiCompatible,
                apiKeyEnvVar: profile.ApiKeyEnvVar,
                currentModel: currentModel,
                models: models);

            entries[providerName] = entry;

            var keyStatus = string.IsNullOrEmpty(apiKey) ? "未配置" : "已配置";
            _logger?.LogInformation("[AgentHubServer] Provider: {Name} | 协议: {Protocol} | API Key: {KeyStatus} | 模型数: {ModelCount}",
                providerName, entry.Protocol, keyStatus, models.Count);
        }

        var catalog = new ProviderCatalog();
        catalog.UpdateProviders(entries, currentProvider);
        _catalog = catalog;
    }

    /// <summary>从 ProfileSettings 构建 ModelEntry 列表。</summary>
    private static IReadOnlyList<ModelEntry> BuildModelEntries(ProfileSettings profile) {
        var list = new List<ModelEntry>();
        if (profile.Models is not null) {
            foreach (var m in profile.Models) {
                list.Add(new ModelEntry(
                    id: m.Id,
                    displayName: string.IsNullOrWhiteSpace(m.DisplayName) ? m.Id : m.DisplayName,
                    contextWindow: m.ContextWindow,
                    description: m.Description));
            }
        }
        return list;
    }

    /// <summary>解析当前模型 — 优先 profile.Model，其次 models 列表第一个。</summary>
    private static ModelEntry? ResolveCurrentModel(ProfileSettings profile, IReadOnlyList<ModelEntry> models) {
        if (string.IsNullOrEmpty(profile.Model)) {
            return models.Count > 0 ? models[0] : null;
        }
        var match = models.FirstOrDefault(m =>
            string.Equals(m.Id, profile.Model, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;
        return new ModelEntry(profile.Model, profile.Model, 0);
    }

    /// <summary>
    /// 模型列表自动拉取 — 并行请求各供应商 modelsEndpoint，拉取后更新 ProviderCatalog。
    /// </summary>
    private async Task FetchModelsAsync() {
        var currentCatalog = _catalog;
        var providerNames = currentCatalog.GetProviderNames();
        if (providerNames.Count == 0) return;

        _logger?.LogInformation("[AgentHubServer] === 开始拉取模型列表 ===");

        var tasks = new List<Task<(string Name, IReadOnlyList<ModelEntry>? Models)>>(providerNames.Count);

        foreach (var name in providerNames) {
            var entry = currentCatalog[name];
            if (string.IsNullOrEmpty(entry.Endpoint)) continue;

            var apiKey = ResolveApiKeyForEntry(entry);
            if (string.IsNullOrEmpty(apiKey)) {
                _logger?.LogWarning("[AgentHubServer] 拉取 {Name} 跳过：未配置 API Key", name);
                continue;
            }

            tasks.Add(FetchModelsForProviderAsync(name, entry.Endpoint, entry.Protocol, apiKey));
        }

        if (tasks.Count == 0) {
            _logger?.LogInformation("[AgentHubServer] 无可拉取的供应商");
            return;
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var updatedEntries = new Dictionary<string, ProviderEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in providerNames) {
            var existing = currentCatalog[name];
            var fetchResult = Array.Find(results, r => r.Name == name);
            var models = fetchResult.Models ?? existing.Models;
            var currentModel = existing.CurrentModel;

            if (currentModel is null && models.Count > 0) {
                currentModel = models[0];
            } else if (currentModel is not null) {
                var match = models.FirstOrDefault(m =>
                    string.Equals(m.Id, currentModel.Id, StringComparison.OrdinalIgnoreCase));
                currentModel = match ?? (models.Count > 0 ? models[0] : null);
            }

            updatedEntries[name] = new ProviderEntry(
                name: existing.Name,
                endpoint: existing.Endpoint,
                protocol: existing.Protocol,
                apiKeyEnvVar: existing.ApiKeyEnvVar,
                currentModel: currentModel,
                models: models);
        }

        var newCatalog = new ProviderCatalog();
        newCatalog.UpdateProviders(updatedEntries, currentCatalog.GetProviderNames().Count > 0
            ? providerNames[0]
            : string.Empty);
        _catalog = newCatalog;

        _logger?.LogInformation("[AgentHubServer] === 模型列表拉取完成 ===");
        foreach (var name in providerNames) {
            var entry = _catalog[name];
            _logger?.LogInformation("[AgentHubServer] {Name}: {Count} 个模型，当前: {Current}",
                name, entry.Models.Count, entry.CurrentModel?.DisplayName ?? "无");
        }
    }

    /// <summary>拉取单个供应商的模型列表。</summary>
    private async Task<(string Name, IReadOnlyList<ModelEntry>? Models)> FetchModelsForProviderAsync(
        string name, string endpoint, string protocol, string apiKey) {
        try {
            var url = $"{endpoint.TrimEnd('/')}/models";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (string.Equals(protocol, ProtocolKindEnumConstants.Anthropic, StringComparison.OrdinalIgnoreCase)) {
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", "2024-10-22");
            } else {
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.SendAsync(request, _cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) {
                _logger?.LogWarning("[AgentHubServer] 拉取 {Name} 返回 {Status}", name, (int)response.StatusCode);
                return (name, null);
            }

            var json = await response.Content.ReadAsStringAsync(_cts.Token).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize(json, ModelFetchJsonContext.Default.ModelListResponseDto);

            if (dto?.Data is null) return (name, null);

            var list = new List<ModelEntry>();
            foreach (var item in dto.Data) {
                if (string.IsNullOrEmpty(item.Id)) continue;
                list.Add(new ModelEntry(
                    id: item.Id,
                    displayName: item.Id,
                    contextWindow: item.ContextLength ?? 0,
                    description: item.Description ?? string.Empty));
            }

            _logger?.LogInformation("[AgentHubServer] {Name} 拉取到 {Count} 个模型", name, list.Count);
            return (name, list);
        } catch (Exception ex) {
            _logger?.LogWarning("[AgentHubServer] 拉取 {Name} 失败: {Error}", name, ex.Message);
            return (name, null);
        }
    }

    /// <summary>
    /// 启动状态报告 — 异步检查各供应商的网络连通性、API Key 配置、模型列表拉取状态。
    /// </summary>
    private async Task ReportStartupStatusAsync() {
        _logger?.LogInformation("[AgentHubServer] === 启动状态检查 ===");

        var currentCatalog = _catalog;
        foreach (var name in currentCatalog.GetProviderNames()) {
            var entry = currentCatalog[name];

            var networkOk = await CheckEndpointConnectivityAsync(entry.Endpoint).ConfigureAwait(false);
            var apiKey = ResolveApiKeyForEntry(entry);
            var apiKeyOk = !string.IsNullOrEmpty(apiKey);
            var modelCount = entry.Models.Count;
            var modelStatus = modelCount > 0 ? $"{modelCount} 个模型" : "空(等待拉取)";

            _logger?.LogInformation("[AgentHubServer] {Name}: 网络={Net} | API Key={Key} | 模型={Models}",
                name,
                networkOk ? "OK" : "不可达",
                apiKeyOk ? "已配置" : "未配置",
                modelStatus);
        }

        _logger?.LogInformation("[AgentHubServer] === 状态检查完成 ===");
    }

    /// <summary>检查端点连通性 — 2秒超时。</summary>
    private static async Task<bool> CheckEndpointConnectivityAsync(string endpoint) {
        try {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            await client.GetAsync(endpoint).ConfigureAwait(false);
            return true;
        } catch {
            return false;
        }
    }

    /// <summary>停止服务器。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task StopAsync(CancellationToken cancellationToken = default) {
        _cts.Cancel();
        _running = false;

        _httpListener.Stop();
        _tcpListener?.Stop();

        await _agentRegistry.CloseAllAsync(cancellationToken).ConfigureAwait(false);

        if (_listenerTask != null) {
            await _listenerTask.ConfigureAwait(false);
            _listenerTask = null;
        }

        if (_tcpListenerTask != null) {
            try { await _tcpListenerTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _tcpListenerTask = null;
        }

        if (_startupStatusTask != null) {
            try { await _startupStatusTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _startupStatusTask = null;
        }

        if (_modelFetchTask != null) {
            try { await _modelFetchTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _modelFetchTask = null;
        }

        _logger?.LogInformation("[AgentHubServer] 服务器已停止");
    }

    /// <summary>
    /// 热重载 Provider 配置 — 全量读取 settings.json + 重建 ProviderCatalog，原子替换。
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

        BuildCatalog(newSettings, newRegistry);

        _logger?.LogInformation("[AgentHubServer] 热重载完成 — Providers: {Count}", _catalog.GetProviderNames().Count);
    }

    /// <summary>
    /// 应用热重载 — 用新的 SettingsJson 和 ProviderDefinitionRegistry 重建 ProviderCatalog，原子替换。
    /// 供测试注入特定配置验证热重载行为。
    /// </summary>
    /// <param name="newSettings">新的 SettingsJson。</param>
    /// <param name="newRegistry">新的 ProviderDefinitionRegistry。</param>
    internal void ApplyReload(SettingsJson newSettings, IProviderDefinitionRegistry newRegistry) {
        BuildCatalog(newSettings, newRegistry);
        _logger?.LogInformation("[AgentHubServer] 热重载完成 — Providers: {Count}", _catalog.GetProviderNames().Count);
    }

    /// <summary>从 ProviderEntry 解析 API Key — 环境变量 > auth.json。</summary>
    private static string ResolveApiKeyForEntry(ProviderEntry entry) {
        if (!string.IsNullOrEmpty(entry.ApiKeyEnvVar)) {
            var envKey = Environment.GetEnvironmentVariable(entry.ApiKeyEnvVar);
            if (!string.IsNullOrEmpty(envKey)) return envKey;
        }

        var authKey = ConfigLoader.LoadStoredApiKeyAsync(entry.Name, FileSystemFactory.Create())
            .GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(authKey)) return authKey;

        return string.Empty;
    }

    /// <summary>从 ProfileSettings + IProviderDefinition 解析 API Key — 环境变量 > auth.json。</summary>
    private static string ResolveApiKey(ProfileSettings profile, IProviderDefinition? definition) {
        if (definition != null) {
            var envKey = definition.ResolveApiKeyFromEnv();
            if (!string.IsNullOrEmpty(envKey)) return envKey;
        }

        if (!string.IsNullOrEmpty(profile.ApiKeyEnvVar)) {
            var envKey = Environment.GetEnvironmentVariable(profile.ApiKeyEnvVar);
            if (!string.IsNullOrEmpty(envKey)) return envKey;
        }

        var authKey = ConfigLoader.LoadStoredApiKeyAsync(profile.Provider ?? string.Empty, FileSystemFactory.Create())
            .GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(authKey)) return authKey;

        return string.Empty;
    }

    private async Task TcpListenAsync() {
        if (_tcpListener is null) return;
        while (!_cts.Token.IsCancellationRequested) {
            try {
                var tcpClient = await _tcpListener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                _ = HandleTcpClientAsync(tcpClient, _cts.Token);
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                _logger?.LogError(ex, "[AgentHubServer] TCP Accept 异常");
            }
        }
    }

    private async Task HandleTcpClientAsync(TcpClient tcpClient, CancellationToken cancellationToken) {
        using var client = tcpClient;
        try {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, new UTF8Encoding(false));

            var agentIdLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(agentIdLine) || !_agentRegistry.IsRegistered(agentIdLine)) {
                var errorMsg = new UTF8Encoding(false).GetBytes("ERROR: agent not found\n");
                await stream.WriteAsync(errorMsg, cancellationToken).ConfigureAwait(false);
                return;
            }

            var okMsg = new UTF8Encoding(false).GetBytes("OK\n");
            await stream.WriteAsync(okMsg, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("[AgentHubServer] TCP 连接: {AgentId}", agentIdLine);

            await foreach (var message in _agentRegistry.ReceiveAsync(agentIdLine, cancellationToken).ConfigureAwait(false)) {
                var json = JsonSerializer.Serialize(message, HubServerJsonContext.Default.AgentMessageRequest);
                var jsonBytes = new UTF8Encoding(false).GetBytes(json);
                var lengthPrefix = BitConverter.GetBytes(jsonBytes.Length);
                if (BitConverter.IsLittleEndian) {
                    Array.Reverse(lengthPrefix);
                }
                await stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(jsonBytes, cancellationToken).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "[AgentHubServer] TCP 连接关闭");
        }
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
                    Providers = _catalog.GetProviderNames().Count
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
        var currentCatalog = _catalog;

        foreach (var name in currentCatalog.GetProviderNames()) {
            var entry = currentCatalog[name];

            foreach (var model in entry.Models) {
                models.Add(new HubModelInfo {
                    Name = model.Id,
                    Provider = name,
                    OwnedBy = name
                });
            }

            if (entry.CurrentModel is not null) {
                var currentId = entry.CurrentModel.Id;
                var exists = models.Any(m => m.Name == currentId && m.Provider == name);
                if (!exists) {
                    models.Add(new HubModelInfo {
                        Name = currentId,
                        Provider = name,
                        OwnedBy = name
                    });
                }
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
        if (string.IsNullOrEmpty(providerName) || !_catalog.TryGet(providerName, out var entry)) {
            context.Response.StatusCode = 400;
            await WriteJsonResponseAsync(context.Response, new HubErrorResponse { Error = $"Provider '{providerName}' not found" }).ConfigureAwait(false);
            return;
        }

        var apiKey = ResolveApiKeyForEntry(entry);
        if (string.IsNullOrEmpty(apiKey)) {
            context.Response.StatusCode = 401;
            await WriteJsonResponseAsync(context.Response, new HubErrorResponse { Error = $"Provider '{providerName}' API Key 未配置" }).ConfigureAwait(false);
            return;
        }

        var isStream = requestBody.Contains("\"stream\":true") || requestBody.Contains("\"stream\": true");

        try {
            using var handler = SocketsHttpHandlerFactory.CreateWithDnsRefresh();
            using var client = new HttpClient(handler) {
                Timeout = TimeSpan.FromMinutes(5),
                BaseAddress = new Uri(entry.Endpoint)
            };

            if (string.Equals(entry.Protocol, ProtocolKindEnumConstants.Anthropic, StringComparison.OrdinalIgnoreCase)) {
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2024-10-22");
            } else {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            }

            using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions") { Content = content };

            var upstreamResponse = await client.SendAsync(
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

    /// <summary>从请求体解析供应商 — 通过模型名匹配 ProviderCatalog 中的模型。</summary>
    private string ResolveProvider(string requestBody) {
        var model = ExtractModel(requestBody);
        var currentCatalog = _catalog;

        foreach (var name in currentCatalog.GetProviderNames()) {
            var entry = currentCatalog[name];
            if (entry.Models.Any(m => string.Equals(m.Id, model, StringComparison.OrdinalIgnoreCase)))
                return name;
            if (entry.CurrentModel is not null && string.Equals(entry.CurrentModel.Id, model, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return currentCatalog.GetProviderNames().Count > 0 ? currentCatalog.GetProviderNames()[0] : string.Empty;
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
            _tcpListener?.Stop();
        }

        if (_listenerTask != null) {
            try { await _listenerTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _listenerTask = null;
        }

        if (_tcpListenerTask != null) {
            try { await _tcpListenerTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _tcpListenerTask = null;
        }

        if (_startupStatusTask != null) {
            try { await _startupStatusTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _startupStatusTask = null;
        }

        if (_modelFetchTask != null) {
            try { await _modelFetchTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            _modelFetchTask = null;
        }

        await _agentRegistry.CloseAllAsync(CancellationToken.None).ConfigureAwait(false);

        _cts.Dispose();
    }
}
