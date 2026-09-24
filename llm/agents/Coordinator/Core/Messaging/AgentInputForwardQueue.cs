namespace Core.Agents.Coordinator;

/// <summary>
/// 子代理用户输入转发队列实现 — 基于 Channel&lt;string&gt;，每 agent 一个独立队列
/// 独立于 AgentMessageBroker，不干扰权限响应路由
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IAgentInputForwardQueue), ServiceLifetime.Singleton)]
public sealed partial class AgentInputForwardQueue : ServiceEntity, JoinCode.Abstractions.Interfaces.IAgentInputForwardQueue {
    private volatile ImmutableDictionary<string, Channel<string>> _queues = ImmutableDictionary<string, Channel<string>>.Empty;
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造子代理用户输入转发队列实例
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    public AgentInputForwardQueue(ILogger? logger = null) {
        _logger = logger;
    }

    /// <summary>
    /// 注册子代理的输入转发队列
    /// </summary>
    public void Register(string agentId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        SetQueue(agentId, Channel.CreateUnbounded<string>());
    }

    /// <summary>
    /// 注销子代理的输入转发队列
    /// </summary>
    public void Unregister(string agentId) {
        if (TryRemoveQueue(agentId, out var channel)) {
            channel.Writer.TryComplete();
        }
    }

    private void SetQueue(string key, Channel<string> value) {
        var current = _queues;
        while (true) {
            var updated = current.SetItem(key, value);
            if (Interlocked.CompareExchange(ref _queues, updated, current) == current) return;
            current = _queues;
        }
    }

    private bool TryRemoveQueue(string key, out Channel<string> value) {
        value = null!;
        var current = _queues;
        while (current.ContainsKey(key)) {
            value = current[key];
            var updated = current.Remove(key);
            if (Interlocked.CompareExchange(ref _queues, updated, current) == current) return true;
            current = _queues;
        }
        return false;
    }

    /// <summary>
    /// 向运行中的子代理追加用户输入
    /// </summary>
    public async Task EnqueueAsync(string agentId, string userInput, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);

        if (_queues.TryGetValue(agentId, out var channel)) {
            await channel.Writer.WriteAsync(userInput, cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("[AgentInputForwardQueue] 用户输入已入队: AgentId={AgentId}, Length={Length}", agentId, userInput.Length);
        } else {
            _logger?.LogWarning("[AgentInputForwardQueue] 子代理 {AgentId} 未注册，丢弃用户输入", agentId);
        }
    }

    /// <summary>
    /// 非阻塞 drain 队列中所有待处理的用户输入
    /// </summary>
    public IReadOnlyList<string> TryDrain(string agentId) {
        if (!_queues.TryGetValue(agentId, out var channel)) {
            return Array.Empty<string>();
        }

        var messages = new List<string>();
        while (channel.Reader.TryRead(out var input)) {
            messages.Add(input);
        }
        return messages;
    }

    /// <summary>
    /// 检查指定子代理是否有待处理的用户输入
    /// </summary>
    public bool HasPending(string agentId) {
        return _queues.TryGetValue(agentId, out var channel) && channel.Reader.Count > 0;
    }
}