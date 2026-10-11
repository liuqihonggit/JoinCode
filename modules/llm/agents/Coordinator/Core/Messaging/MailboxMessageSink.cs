namespace Core.Agents.Coordinator;

/// <summary>
/// 邮箱消息接收槽 — 接收 MailboxPoller 投递的跨进程消息，直接入队到内存 Channel。
/// <para>断开 Broker→Poller→Broker 循环：消息已在文件邮箱中，不再次持久化。</para>
/// <para>注入 IMailbox（InProcessMailbox），调用 DeliverInboundAsync 只入队不持久化。</para>
/// </summary>
[Register(typeof(IMailboxMessageSink), ServiceLifetime.Singleton)]
public sealed class MailboxMessageSink : IMailboxMessageSink {
    private readonly InProcessMailbox _mailbox;
    private readonly ILogger<MailboxMessageSink>? _logger;

    /// <summary>
    /// 创建邮箱消息接收槽
    /// </summary>
    /// <param name="mailbox">进程内邮箱（IMailbox 实现）</param>
    /// <param name="logger">日志记录器</param>
    public MailboxMessageSink(IMailbox mailbox, ILogger<MailboxMessageSink>? logger = null) {
        _mailbox = mailbox as InProcessMailbox
            ?? throw new InvalidOperationException("IMailbox must be InProcessMailbox for MailboxMessageSink");
        _logger = logger;
    }

    /// <summary>
    /// 投递跨进程消息到目标 Agent 的内存 Channel — 不持久化到文件邮箱
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="message">要投递的消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task DeliverAsync(string agentId, CoordinatorMessage message, CancellationToken cancellationToken = default) {
        try {
            await _mailbox.DeliverInboundAsync(agentId, message, cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("Inbound message delivered to {AgentId} from {FromId}", agentId, message.FromAgentId);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogWarning(ex, "Failed to deliver inbound message to {AgentId}", agentId);
        }
    }
}