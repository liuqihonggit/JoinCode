namespace Core.Agents.Coordinator;

/// <summary>
/// 子代理用户输入转发队列实现 — 基于 Channel&lt;string&gt;，每 agent 一个独立队列
/// 独立于 AgentMessageBroker，不干扰权限响应路由
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IAgentInputForwardQueue))]
public sealed partial class AgentInputForwardQueue : ServiceEntity, JoinCode.Abstractions.Interfaces.IAgentInputForwardQueue
{
    private readonly ConcurrentDictionary<string, Channel<string>> _queues;
    private readonly ILogger? _logger;

    public AgentInputForwardQueue(ILogger? logger = null)
    {
        _queues = new ConcurrentDictionary<string, Channel<string>>();
        _logger = logger;
    }

    /// <summary>
    /// 注册子代理的输入转发队列
    /// </summary>
    public void Register(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        _queues[agentId] = Channel.CreateUnbounded<string>();
    }

    /// <summary>
    /// 注销子代理的输入转发队列
    /// </summary>
    public void Unregister(string agentId)
    {
        if (_queues.TryRemove(agentId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// 向运行中的子代理追加用户输入
    /// </summary>
    public async Task EnqueueAsync(string agentId, string userInput, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);

        if (_queues.TryGetValue(agentId, out var channel))
        {
            await channel.Writer.WriteAsync(userInput, cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("[AgentInputForwardQueue] 用户输入已入队: AgentId={AgentId}, Length={Length}", agentId, userInput.Length);
        }
        else
        {
            _logger?.LogWarning("[AgentInputForwardQueue] 子代理 {AgentId} 未注册，丢弃用户输入", agentId);
        }
    }

    /// <summary>
    /// 非阻塞 drain 队列中所有待处理的用户输入
    /// </summary>
    public IReadOnlyList<string> TryDrain(string agentId)
    {
        if (!_queues.TryGetValue(agentId, out var channel))
        {
            return Array.Empty<string>();
        }

        var messages = new List<string>();
        while (channel.Reader.TryRead(out var input))
        {
            messages.Add(input);
        }
        return messages;
    }

    /// <summary>
    /// 检查指定子代理是否有待处理的用户输入
    /// </summary>
    public bool HasPending(string agentId)
    {
        return _queues.TryGetValue(agentId, out var channel) && channel.Reader.Count > 0;
    }
}
