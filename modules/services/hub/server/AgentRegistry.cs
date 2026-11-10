
namespace Core.Hub.Server;

public sealed partial class AgentRegistry {
    private readonly ConcurrentDictionary<string, AgentInfo> _agents = new();
    private readonly ConcurrentDictionary<string, Channel<AgentMessageRequest>> _mailboxes = new();
    private readonly ILogger? _logger;
    private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(90);
    private readonly TimeSpan _scanInterval = TimeSpan.FromSeconds(60);
    private Timer? _heartbeatScanner;

    /// <summary>获取已注册 Agent 数量。</summary>
    public int Count => _agents.Count;

    /// <summary>构造 AgentRegistry。</summary>
    /// <param name="logger">日志记录器（可选）。</param>
    public AgentRegistry(ILogger? logger = null) {
        _logger = logger;
        StartHeartbeatScanner();
    }

    /// <summary>注册 Agent。</summary>
    /// <param name="agentId">Agent 标识符。</param>
    /// <param name="sessionId">会话标识符（可选）。</param>
    /// <param name="capabilities">能力标签列表（可选，用于按能力路由）。</param>
    public void Register(string agentId, string? sessionId = null, List<string>? capabilities = null) {
        var now = DateTimeOffset.UtcNow;
        var info = new AgentInfo {
            AgentId = agentId,
            SessionId = sessionId,
            ConnectedAt = now,
            LastHeartbeatAt = now,
            Capabilities = capabilities ?? [],
            PendingMessages = 0
        };
        _agents[agentId] = info;
        _mailboxes[agentId] = Channel.CreateUnbounded<AgentMessageRequest>();
        _logger?.LogInformation("[AgentRegistry] Agent 注册: {AgentId} (capabilities={Caps})",
            agentId, capabilities is null ? "none" : string.Join(",", capabilities));
    }

    /// <summary>注销 Agent。</summary>
    /// <param name="agentId">Agent 标识符。</param>
    public void Unregister(string agentId) {
        _agents.TryRemove(agentId, out _);
        if (_mailboxes.TryRemove(agentId, out var channel)) {
            channel.Writer.TryComplete();
        }
        _logger?.LogInformation("[AgentRegistry] Agent 注销: {AgentId}", agentId);
    }

    /// <summary>获取所有已注册 Agent 信息。</summary>
    public List<AgentInfo> GetAll() => _agents.Values.ToList();

    /// <summary>检查 Agent 是否已注册。</summary>
    /// <param name="agentId">Agent 标识符。</param>
    public bool IsRegistered(string agentId) => _agents.ContainsKey(agentId);

    /// <summary>向指定 Agent 投递消息。</summary>
    /// <param name="toAgentId">目标 Agent 标识符。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<bool> DeliverMessageAsync(string toAgentId, AgentMessageRequest message, CancellationToken ct) {
        if (!_mailboxes.TryGetValue(toAgentId, out var channel)) {
            _logger?.LogWarning("[AgentRegistry] Agent 不存在: {AgentId}", toAgentId);
            return false;
        }
        await channel.Writer.WriteAsync(message, ct).ConfigureAwait(false);
        if (_agents.TryGetValue(toAgentId, out var info)) {
            _agents[toAgentId] = info with { PendingMessages = info.PendingMessages + 1 };
        }
        return true;
    }

    /// <summary>向所有 Agent 广播消息（排除发送方）。</summary>
    /// <param name="fromAgentId">发送方 Agent 标识符（排除）。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task BroadcastMessageAsync(string? fromAgentId, AgentMessageRequest message, CancellationToken ct) {
        foreach (var (agentId, channel) in _mailboxes) {
            if (agentId == fromAgentId) continue;
            await channel.Writer.WriteAsync(message, ct).ConfigureAwait(false);
            if (_agents.TryGetValue(agentId, out var info)) {
                _agents[agentId] = info with { PendingMessages = info.PendingMessages + 1 };
            }
        }
    }

    /// <summary>
    /// 按能力路由消息 — 在具有指定能力的 Agent 中选择待处理消息最少的（负载均衡）。
    /// </summary>
    /// <param name="capability">目标能力标签。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>投递成功返回目标 AgentId，无匹配 Agent 返回 null。</returns>
    public async Task<string?> RouteByCapabilityAsync(string capability, AgentMessageRequest message, CancellationToken ct) {
        var candidates = _agents.Values
            .Where(a => a.Capabilities.Contains(capability))
            .OrderBy(a => a.PendingMessages)
            .ToList();

        if (candidates.Count == 0) {
            _logger?.LogWarning("[AgentRegistry] 无具有能力 '{Capability}' 的 Agent", capability);
            return null;
        }

        var target = candidates[0];
        var delivered = await DeliverMessageAsync(target.AgentId, message, ct).ConfigureAwait(false);
        return delivered ? target.AgentId : null;
    }

    /// <summary>异步接收指定 Agent 的消息流。</summary>
    /// <param name="agentId">Agent 标识符。</param>
    /// <param name="ct">取消令牌。</param>
    public IAsyncEnumerable<AgentMessageRequest> ReceiveAsync(string agentId, CancellationToken ct) {
        if (!_mailboxes.TryGetValue(agentId, out var channel)) {
            throw new InvalidOperationException($"Agent '{agentId}' not registered");
        }
        return channel.Reader.ReadAllAsync(ct);
    }

    /// <summary>更新 Agent 心跳时间。</summary>
    /// <param name="agentId">Agent 标识符。</param>
    /// <returns>心跳更新成功返回 true，Agent 不存在返回 false。</returns>
    public bool UpdateHeartbeat(string agentId) {
        if (!_agents.TryGetValue(agentId, out var info)) {
            return false;
        }
        _agents[agentId] = info with { LastHeartbeatAt = DateTimeOffset.UtcNow };
        return true;
    }

    /// <summary>启动后台心跳超时扫描器。</summary>
    private void StartHeartbeatScanner() {
        _heartbeatScanner = new Timer(_ => ScanTimeoutAgents(), null, _scanInterval, _scanInterval);
    }

    /// <summary>扫描并注销心跳超时的 Agent。</summary>
    internal void ScanTimeoutAgents() {
        var now = DateTimeOffset.UtcNow;
        var timeoutAgents = _agents.Values
            .Where(a => now - a.LastHeartbeatAt > _heartbeatTimeout)
            .Select(a => a.AgentId)
            .ToList();

        foreach (var agentId in timeoutAgents) {
            _logger?.LogWarning("[AgentRegistry] Agent 心跳超时，自动注销: {AgentId}", agentId);
            Unregister(agentId);
        }
    }

    /// <summary>关闭所有 Agent 连接。</summary>
    /// <param name="ct">取消令牌。</param>
    public async Task CloseAllAsync(CancellationToken ct) {
        _heartbeatScanner?.Dispose();
        foreach (var (agentId, channel) in _mailboxes) {
            channel.Writer.TryComplete();
        }
        _agents.Clear();
        _mailboxes.Clear();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>Agent 信息 DTO。</summary>
public sealed record AgentInfo {
    /// <summary>Agent 标识符。</summary>
    [JsonPropertyName("agent_id")]
    public string AgentId { get; init; } = string.Empty;

    /// <summary>会话标识符。</summary>
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    /// <summary>连接时间。</summary>
    [JsonPropertyName("connected_at")]
    public DateTimeOffset ConnectedAt { get; init; }

    /// <summary>最后心跳时间。</summary>
    [JsonPropertyName("last_heartbeat_at")]
    public DateTimeOffset LastHeartbeatAt { get; init; }

    /// <summary>能力标签列表（用于按能力路由）。</summary>
    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; init; } = [];

    /// <summary>待处理消息数量（用于负载均衡）。</summary>
    [JsonPropertyName("pending_messages")]
    public int PendingMessages { get; init; }
}
