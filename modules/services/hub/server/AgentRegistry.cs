
namespace Core.Hub.Server;

public sealed partial class AgentRegistry {
    private readonly ConcurrentDictionary<string, AgentInfo> _agents = new();
    private readonly ConcurrentDictionary<string, Channel<AgentMessageRequest>> _mailboxes = new();
    private readonly ILogger? _logger;

    /// <summary>获取已注册 Agent 数量。</summary>
    public int Count => _agents.Count;

    /// <summary>构造 AgentRegistry。</summary>
    /// <param name="logger">日志记录器（可选）。</param>
    public AgentRegistry(ILogger? logger = null) {
        _logger = logger;
    }

    /// <summary>注册 Agent。</summary>
    /// <param name="agentId">Agent 标识符。</param>
    /// <param name="sessionId">会话标识符（可选）。</param>
    public void Register(string agentId, string? sessionId = null) {
        var info = new AgentInfo {
            AgentId = agentId,
            SessionId = sessionId,
            ConnectedAt = DateTimeOffset.UtcNow
        };
        _agents[agentId] = info;
        _mailboxes[agentId] = Channel.CreateUnbounded<AgentMessageRequest>();
        _logger?.LogInformation("[AgentRegistry] Agent 注册: {AgentId}", agentId);
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
        }
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

    /// <summary>关闭所有 Agent 连接。</summary>
    /// <param name="ct">取消令牌。</param>
    public async Task CloseAllAsync(CancellationToken ct) {
        foreach (var (agentId, channel) in _mailboxes) {
            channel.Writer.TryComplete();
        }
        _agents.Clear();
        _mailboxes.Clear();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>Agent 信息 DTO。</summary>
public sealed class AgentInfo {
    /// <summary>Agent 标识符。</summary>
    [JsonPropertyName("agent_id")]
    public string AgentId { get; init; } = string.Empty;

    /// <summary>会话标识符。</summary>
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    /// <summary>连接时间。</summary>
    [JsonPropertyName("connected_at")]
    public DateTimeOffset ConnectedAt { get; init; }
}
