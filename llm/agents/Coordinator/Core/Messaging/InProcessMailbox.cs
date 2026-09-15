namespace Core.Agents.Coordinator;

/// <summary>
/// 进程内邮箱实现 — 基于 Channel&lt;CoordinatorMessage&gt; 的内存消息传递，可选持久化到文件邮箱
/// </summary>
[Register(typeof(IMailbox), ServiceLifetime.Singleton)]
public sealed partial class InProcessMailbox : ServiceEntity, IMailbox
{
    private readonly ILogger? _logger;
    private readonly ITeammateMailboxService? _mailboxService;
    private readonly ConcurrentDictionary<string, Channel<CoordinatorMessage>> _messageChannels;
    private readonly ConcurrentDictionary<string, string> _agentSessions;

    /// <summary>
    /// 构造进程内邮箱实例
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="mailboxService">可选队友邮箱服务，用于将消息持久化到跨进程邮箱</param>
    public InProcessMailbox(ILogger? logger = null, ITeammateMailboxService? mailboxService = null)
    {
        _logger = logger;
        _mailboxService = mailboxService;
        _messageChannels = new ConcurrentDictionary<string, Channel<CoordinatorAgentMessage>>();
        _agentSessions = new ConcurrentDictionary<string, string>();
    }

    /// <summary>
    /// 注册 Agent 邮箱，创建对应的内存 Channel 并可选记录会话 ID
    /// </summary>
    /// <param name="agentId">Agent 标识</param>
    /// <param name="sessionId">可选会话 ID，用于持久化到文件邮箱</param>
    public void RegisterAgent(string agentId, string? sessionId = null)
    {
        _messageChannels[agentId] = Channel.CreateUnbounded<CoordinatorAgentMessage>();

        if (sessionId is not null)
        {
            _agentSessions[agentId] = sessionId;
        }
    }

    /// <summary>
    /// 注销 Agent 邮箱，完成对应 Channel 并移除会话映射
    /// </summary>
    /// <param name="agentId">Agent 标识</param>
    public void UnregisterAgent(string agentId)
    {
        if (_messageChannels.TryRemove(agentId, out var channel))
        {
            channel.Writer.Complete();
        }

        _agentSessions.TryRemove(agentId, out _);
    }

    /// <summary>
    /// 向指定 Agent 投递消息，写入内存 Channel 并尝试持久化到文件邮箱
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="message">要投递的消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功写入内存 Channel（未注册 Agent 时返回 false）</returns>
    public async Task<bool> SendAsync(string agentId, CoordinatorAgentMessage message, CancellationToken cancellationToken = default)
    {
        var channelDelivered = false;

        if (_messageChannels.TryGetValue(agentId, out var channel))
        {
            await channel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            channelDelivered = true;
        }

        await PersistToMailboxAsync(agentId, message, cancellationToken).ConfigureAwait(false);

        return channelDelivered;
    }

    /// <summary>
    /// 向所有已注册 Agent 广播消息（跳过消息发送者自身）
    /// </summary>
    /// <param name="message">要广播的消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task BroadcastAsync(CoordinatorAgentMessage message, CancellationToken cancellationToken = default)
    {
        var tasks = _messageChannels
            .Where(kvp => kvp.Key != message.FromAgentId)
            .Select(kvp => SendAsync(kvp.Key, message, cancellationToken));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取指定 Agent 的消息接收流；未注册时返回空流
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>消息异步枚举流</returns>
    public IAsyncEnumerable<CoordinatorAgentMessage> ReceiveAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (_messageChannels.TryGetValue(agentId, out var channel))
        {
            return channel.Reader.ReadAllAsync(cancellationToken);
        }
        return AsyncEnumerable.Empty<CoordinatorAgentMessage>();
    }

    /// <summary>
    /// 获取所有已注册 Agent 的标识集合
    /// </summary>
    /// <returns>已注册 Agent ID 集合</returns>
    public IEnumerable<string> GetRegisteredAgents() => _messageChannels.Keys;

    /// <summary>
    /// 获取指定 Agent 关联的会话 ID
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <returns>会话 ID；未关联时返回 null</returns>
    public string? GetSessionId(string agentId)
    {
        return _agentSessions.GetValueOrDefault(agentId);
    }

    /// <summary>
    /// 投递跨进程入站消息 — 只写入内存 Channel，不持久化到文件邮箱。
    /// <para>由 MailboxMessageSink 调用，断开 Broker→Poller→Broker 循环。</para>
    /// <para>消息已在文件邮箱中，无需再次持久化。</para>
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="message">要投递的消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task DeliverInboundAsync(string agentId, CoordinatorAgentMessage message, CancellationToken cancellationToken = default)
    {
        if (_messageChannels.TryGetValue(agentId, out var channel))
        {
            await channel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PersistToMailboxAsync(string agentId, CoordinatorAgentMessage message, CancellationToken cancellationToken)
    {
        if (_mailboxService is null) return;

        var sessionId = _agentSessions.GetValueOrDefault(agentId);
        if (string.IsNullOrEmpty(sessionId)) return;

        try
        {
            var request = new MailboxSendRequest
            {
                FromAgentId = message.FromAgentId,
                ToAgentId = agentId,
                MessageType = message.MessageType,
                Content = message.Content,
                SessionId = sessionId
            };

            await _mailboxService.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to persist message to mailbox for {AgentId}", agentId);
        }
    }
}
