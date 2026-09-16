namespace Core.Agents.Coordinator;

/// <summary>
/// 文件邮箱 — 继承 <see cref="MailboxBase{TMessage}"/>，消息持久化到 JSONL 文件，跨进程可见。
/// <para>继承 MailboxBase，复用双工+背压+水位线+超时能力。</para>
/// <para>持久化：通过 <see cref="ITeammateMailboxService"/> 写入 JSONL 文件，跨进程可见。</para>
/// <para>本地投递：消息同时写入内存 Channel（实时消费）和文件（跨进程消费）。</para>
/// <para>去重：用 MessageId 去重，同一 Agent 的重复消息只投递一次。</para>
/// <para>与 <see cref="InProcessMailbox"/> 的区别：FileMailbox 强制持久化（mailboxService 必须注入），InProcessMailbox 可选持久化。</para>
/// </summary>
[Register(typeof(FileMailbox), ServiceLifetime.Singleton)]
public sealed partial class FileMailbox : MailboxBase<CoordinatorMessage>, IMailbox
{
    private readonly ITeammateMailboxService _mailboxService;
    private readonly ILogger<FileMailbox>? _logger;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _deliveredMessageIds;

    /// <summary>
    /// 构造文件邮箱。
    /// </summary>
    /// <param name="mailboxService">队友邮箱服务（强制注入，提供文件持久化）</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="commandBackpressure">命令通道背压</param>
    /// <param name="agentBackpressure">Agent 消息通道背压</param>
    public FileMailbox(
        ITeammateMailboxService mailboxService,
        ILogger<FileMailbox>? logger = null,
        ActorBackpressure? commandBackpressure = null,
        ActorBackpressure? agentBackpressure = null)
        : base(
            commandBackpressure ?? ActorBackpressure.CodingAgentTask,
            agentBackpressure ?? DefaultAgentBackpressure,
            outputCapacity: 128)
    {
        _mailboxService = mailboxService ?? throw new ArgumentNullException(nameof(mailboxService));
        _logger = logger;
        _deliveredMessageIds = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>();
    }

    /// <summary>
    /// 注册 Agent 邮箱 — fire-and-forget 异步注册。
    /// </summary>
    public void RegisterAgent(string agentId, string? sessionId = null)
        => _ = RegisterAgentAsync(agentId, sessionId);

    /// <summary>
    /// 注销 Agent 邮箱 — fire-and-forget 异步注销。
    /// </summary>
    public void UnregisterAgent(string agentId)
        => _ = UnregisterAgentAsync(agentId);

    /// <summary>
    /// 发送消息 — tell 异步，本地投递 + 文件持久化。
    /// <para>用 MessageId 去重：重复消息只投递一次。</para>
    /// </summary>
    public async Task<bool> SendAsync(string agentId, CoordinatorMessage message, CancellationToken cancellationToken = default)
    {
        if (IsDuplicate(agentId, message.MessageId))
        {
            _logger?.LogDebug("FileMailbox: duplicate message {MessageId} skipped for {AgentId}", message.MessageId, agentId);
            return false;
        }

        await TellAsync(agentId, message, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 广播消息到所有已注册 Agent。
    /// </summary>
    public async Task BroadcastAsync(CoordinatorMessage message, CancellationToken cancellationToken = default)
        => await TellBroadcastAsync(message, message.FromAgentId, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// 发送命令处理 — 本地投递 + 文件持久化。
    /// </summary>
    protected override async ValueTask HandleSendAsync(string agentId, CoordinatorMessage message, CancellationToken ct)
    {
        if (IsDuplicate(agentId, message.MessageId)) return;

        DeliverToAgent(agentId, message);
        await PersistToMailboxAsync(agentId, message, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 广播命令处理 — 本地广播 + 文件持久化。
    /// </summary>
    protected override async ValueTask HandleBroadcastAsync(CoordinatorMessage message, string? excludeAgentId, CancellationToken ct)
    {
        foreach (var agentId in GetRegisteredAgents())
        {
            if (agentId == excludeAgentId) continue;
            if (IsDuplicate(agentId, message.MessageId)) continue;
            DeliverToAgent(agentId, message);
            await PersistToMailboxAsync(agentId, message, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 检查消息是否已投递 — 用 MessageId 去重。
    /// </summary>
    private bool IsDuplicate(string agentId, string messageId)
    {
        var deliveredSet = _deliveredMessageIds.GetOrAdd(agentId, _ => new ConcurrentDictionary<string, byte>());
        return !deliveredSet.TryAdd(messageId, 0);
    }

    /// <summary>
    /// 持久化消息到文件邮箱。
    /// </summary>
    private async Task PersistToMailboxAsync(string agentId, CoordinatorMessage message, CancellationToken cancellationToken)
    {
        var sessionId = GetSessionId(agentId);
        if (string.IsNullOrEmpty(sessionId))
        {
            _logger?.LogDebug("FileMailbox: no session for {AgentId}, skip persist", agentId);
            return;
        }

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
            _logger?.LogWarning(ex, "FileMailbox: persist failed for {AgentId}", agentId);
        }
    }
}
