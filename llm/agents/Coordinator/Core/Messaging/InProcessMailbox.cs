namespace Core.Agents.Coordinator;

/// <summary>
/// 进程内邮箱实现 — 继承 <see cref="MailboxBase{TMessage}"/>，复用双工+背压+水位线+超时能力。
/// <para>基于内存 Channel 传递消息，可选持久化到文件邮箱。</para>
/// <para>用 MessageId 去重：同一 Agent 的重复消息（相同 MessageId）只投递一次。</para>
/// <para>背压：命令通道用 <see cref="ActorBackpressure.CodingAgentTask"/>，Agent 通道用 <see cref="MailboxBase{TMessage}.DefaultAgentBackpressure"/>。</para>
/// <para>水位线：Agent Channel 达到高水位线时触发 <see cref="WatermarkReachedEvt{TMessage}"/>，生产方应限速。</para>
/// </summary>
[Register(typeof(IMailbox), ServiceLifetime.Singleton)]
public sealed partial class InProcessMailbox : MailboxBase<CoordinatorMessage>, IMailbox
{
    private readonly ILogger? _logger;
    private readonly ITeammateMailboxService? _mailboxService;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _deliveredMessageIds;

    /// <summary>
    /// 构造进程内邮箱实例
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="mailboxService">可选队友邮箱服务，用于将消息持久化到跨进程邮箱</param>
    /// <param name="commandBackpressure">命令通道背压（null=<see cref="ActorBackpressure.CodingAgentTask"/>）</param>
    /// <param name="agentBackpressure">Agent 消息通道背压（null=默认 256 容量）</param>
    public InProcessMailbox(
        ILogger? logger = null,
        ITeammateMailboxService? mailboxService = null,
        ActorBackpressure? commandBackpressure = null,
        ActorBackpressure? agentBackpressure = null)
        : base(
            commandBackpressure ?? ActorBackpressure.CodingAgentTask,
            agentBackpressure ?? DefaultAgentBackpressure,
            outputCapacity: 128)
    {
        _logger = logger;
        _mailboxService = mailboxService;
        _deliveredMessageIds = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>();
    }

    /// <summary>
    /// 注册 Agent 邮箱 — fire-and-forget 异步注册，不阻塞调用方。
    /// </summary>
    /// <param name="agentId">Agent 标识</param>
    /// <param name="sessionId">可选会话 ID</param>
    public void RegisterAgent(string agentId, string? sessionId = null)
        => _ = RegisterAgentAsync(agentId, sessionId);

    /// <summary>
    /// 注销 Agent 邮箱 — 清理去重记录 + fire-and-forget 异步注销。
    /// </summary>
    /// <param name="agentId">Agent 标识</param>
    public void UnregisterAgent(string agentId)
    {
        _deliveredMessageIds.TryRemove(agentId, out _);
        _ = UnregisterAgentAsync(agentId, CancellationToken.None);
    }

    /// <summary>
    /// 向指定 Agent 投递消息 — tell 异步，不等待响应。
    /// <para>用 MessageId 去重：重复消息只投递一次。</para>
    /// <para>可选持久化到文件邮箱（配置了 mailboxService 时）。</para>
    /// </summary>
    /// <param name="agentId">目标 Agent</param>
    /// <param name="message">消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功投递（重复消息返回 false）</returns>
    public async Task<bool> SendAsync(string agentId, CoordinatorMessage message, CancellationToken cancellationToken = default)
    {
        if (IsDuplicate(agentId, message.MessageId))
        {
            _logger?.LogDebug("Duplicate message {MessageId} skipped for {AgentId}", message.MessageId, agentId);
            return false;
        }

        DeliverToAgent(agentId, message);
        await PersistToMailboxAsync(agentId, message, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 广播消息到所有已注册 Agent（跳过发送者）。
    /// </summary>
    public async Task BroadcastAsync(CoordinatorMessage message, CancellationToken cancellationToken = default)
        => await TellBroadcastAsync(message, message.FromAgentId, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// 投递跨进程入站消息 — 只写入本地 Agent Channel，不持久化。
    /// <para>由 MailboxMessageSink 调用，断开 Broker→Poller→Broker 循环。</para>
    /// <para>用 MessageId 去重：重复消息只投递一次。</para>
    /// </summary>
    public async Task DeliverInboundAsync(string agentId, CoordinatorMessage message, CancellationToken cancellationToken = default)
    {
        if (IsDuplicate(agentId, message.MessageId))
        {
            _logger?.LogDebug("Duplicate inbound message {MessageId} skipped for {AgentId}", message.MessageId, agentId);
            return;
        }

        DeliverToAgent(agentId, message);
        await Task.Yield();
    }

    /// <summary>
    /// 发送命令处理 — 加去重后投递到本地 Agent Channel。
    /// </summary>
    protected override ValueTask HandleSendAsync(string agentId, CoordinatorMessage message, CancellationToken ct)
    {
        if (IsDuplicate(agentId, message.MessageId)) return ValueTask.CompletedTask;
        DeliverToAgent(agentId, message);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 检查消息是否已投递给指定 Agent — 用 MessageId 去重。
    /// </summary>
    private bool IsDuplicate(string agentId, string messageId)
    {
        var deliveredSet = _deliveredMessageIds.GetOrAdd(agentId, _ => new ConcurrentDictionary<string, byte>());
        return !deliveredSet.TryAdd(messageId, 0);
    }

    /// <summary>
    /// 持久化消息到文件邮箱 — 可选，配置了 mailboxService 时生效。
    /// </summary>
    private async Task PersistToMailboxAsync(string agentId, CoordinatorMessage message, CancellationToken cancellationToken)
    {
        if (_mailboxService is null) return;

        var sessionId = GetSessionId(agentId);
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
