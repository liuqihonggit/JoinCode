namespace Api.Chat;

/// <summary>
/// Hub Agent 客户端 — 管理 jcc.exe 实例与 AgentHubServer 的完整生命周期交互。
/// 包括：注册、注销、心跳保活、SSE 消息接收。
/// </summary>
public sealed partial class HubAgentClient : IAsyncDisposable {
    private const string HubEndpoint = "http://localhost:3457";
    private const int HeartbeatIntervalMs = 30_000;

    private readonly HttpClient _httpClient;
    private readonly string _agentId;
    private readonly List<string> _capabilities;
    private readonly ILogger<HubAgentClient>? _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _heartbeatTask;
    private Task? _sseReceiveTask;
    private int _disposed;

    /// <summary>当前 Agent 标识符。</summary>
    public string AgentId => _agentId;

    /// <summary>
    /// 构造 HubAgentClient。
    /// </summary>
    /// <param name="agentId">Agent 标识符（格式：jcc-{PID}-{短GUID}）。</param>
    /// <param name="capabilities">能力标签列表。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public HubAgentClient(string agentId, List<string>? capabilities = null, ILogger<HubAgentClient>? logger = null) {
        _agentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
        _capabilities = capabilities ?? [];
        _logger = logger;
        _httpClient = new HttpClient { BaseAddress = new Uri(HubEndpoint), Timeout = TimeSpan.FromSeconds(5) };
    }

    /// <summary>注册 Agent 到 Hub 服务端。</summary>
    public async Task<bool> RegisterAsync(CancellationToken ct = default) {
        var request = new AgentRegisterRequest {
            AgentId = _agentId,
            Capabilities = _capabilities
        };
        var json = JsonSerializer.Serialize(request, HubAgentJsonContext.Default.AgentRegisterRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try {
            var response = await _httpClient.PostAsync("/agent/register", content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                _logger?.LogError("[HubAgentClient] 注册失败: {StatusCode}", response.StatusCode);
                return false;
            }
            _logger?.LogInformation("[HubAgentClient] Agent 注册成功: {AgentId}", _agentId);
            return true;
        } catch (Exception ex) {
            _logger?.LogError(ex, "[HubAgentClient] 注册异常");
            return false;
        }
    }

    /// <summary>注销 Agent。</summary>
    public async Task UnregisterAsync(CancellationToken ct = default) {
        var request = new AgentUnregisterRequest { AgentId = _agentId };
        var json = JsonSerializer.Serialize(request, HubAgentJsonContext.Default.AgentUnregisterRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try {
            await _httpClient.PostAsync("/agent/unregister", content, ct).ConfigureAwait(false);
            _logger?.LogInformation("[HubAgentClient] Agent 注销: {AgentId}", _agentId);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[HubAgentClient] 注销异常");
        }
    }

    /// <summary>发送心跳。</summary>
    public async Task<bool> SendHeartbeatAsync(CancellationToken ct = default) {
        var request = new AgentHeartbeatRequest { AgentId = _agentId };
        var json = JsonSerializer.Serialize(request, HubAgentJsonContext.Default.AgentHeartbeatRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try {
            var response = await _httpClient.PostAsync("/agent/heartbeat", content, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[HubAgentClient] 心跳异常");
            return false;
        }
    }

    /// <summary>启动心跳定时器和 SSE 消息接收。</summary>
    /// <param name="onMessageReceived">收到消息时的回调。</param>
    public void StartBackgroundTasks(Action<string>? onMessageReceived = null) {
        _heartbeatTask = RunHeartbeatLoopAsync(_cts.Token);
        if (onMessageReceived is not null) {
            _sseReceiveTask = RunSseReceiveLoopAsync(onMessageReceived, _cts.Token);
        }
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            try {
                await Task.Delay(HeartbeatIntervalMs, ct).ConfigureAwait(false);
                await SendHeartbeatAsync(ct).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "[HubAgentClient] 心跳循环异常");
            }
        }
    }

    private async Task RunSseReceiveLoopAsync(Action<string> onMessageReceived, CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            try {
                using var sseRequest = new HttpRequestMessage(HttpMethod.Get, $"/agent/messages/{_agentId}");
                sseRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
                using var sseResponse = await _httpClient.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                if (!sseResponse.IsSuccessStatusCode) {
                    _logger?.LogWarning("[HubAgentClient] SSE 连接失败: {StatusCode}, 5s 后重连", sseResponse.StatusCode);
                    await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                    continue;
                }

                using var stream = await sseResponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream);

                string? line;
                while (!ct.IsCancellationRequested) {
                    line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line == null) break;
                    if (!line.StartsWith("data: ")) continue;

                    var data = line[6..];
                    onMessageReceived(data);
                }

                _logger?.LogWarning("[HubAgentClient] SSE 连接断开, 3s 后重连");
                await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "[HubAgentClient] SSE 接收异常, 5s 后重连");
                try {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    break;
                }
            }
        }
    }

    /// <summary>生成唯一 AgentId。</summary>
    public static string GenerateAgentId() {
        var pid = Environment.ProcessId;
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        return $"jcc-{pid}-{shortGuid}";
    }

    /// <summary>异步释放资源 — 注销 Agent + 停止后台任务。</summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _cts.Cancel();

        try {
            await UnregisterAsync(CancellationToken.None).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[HubAgentClient] 注销异常");
        }

        try {
            if (_heartbeatTask != null) await _heartbeatTask.ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[HubAgentClient] 心跳任务等待异常");
        }

        try {
            if (_sseReceiveTask != null) await _sseReceiveTask.ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[HubAgentClient] SSE接收任务等待异常");
        }

        _cts.Dispose();
        _httpClient.Dispose();
    }
}

/// <summary>Agent 注册请求 DTO（客户端侧）。</summary>
public sealed class AgentRegisterRequest {
    /// <summary>Agent 标识符。</summary>
    [JsonPropertyName("agent_id")]
    public string AgentId { get; init; } = string.Empty;

    /// <summary>能力标签列表。</summary>
    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; init; } = [];
}

/// <summary>Agent 注销请求 DTO（客户端侧）。</summary>
public sealed class AgentUnregisterRequest {
    /// <summary>Agent 标识符。</summary>
    [JsonPropertyName("agent_id")]
    public string AgentId { get; init; } = string.Empty;
}

/// <summary>Agent 心跳请求 DTO（客户端侧）。</summary>
public sealed class AgentHeartbeatRequest {
    /// <summary>Agent 标识符。</summary>
    [JsonPropertyName("agent_id")]
    public string AgentId { get; init; } = string.Empty;
}

/// <summary>HubAgentClient JSON 序列化上下文。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AgentRegisterRequest))]
[JsonSerializable(typeof(AgentUnregisterRequest))]
[JsonSerializable(typeof(AgentHeartbeatRequest))]
public partial class HubAgentJsonContext : JsonSerializerContext;
