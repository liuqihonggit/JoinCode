namespace Core.Agents.Coordinator;

/// <summary>
/// 邮箱中枢 — 统一消息传递入口，按 MailboxKind 路由到 InProcessMailbox 或文件邮箱。
/// 对齐 claude code 的 teammateMailbox + AgentTool 双模式设计。
/// 渐进式引入：消费者可选用 MailboxHub 替代直接调用 IMailbox 或 ITeammateMailboxService。
/// </summary>
[Register]
[AllowSkipEntity("邮箱中枢是路由组件，无独立生命周期，不需要 Entity 追踪")]
public sealed partial class MailboxHub
{
    private readonly IMailbox _inProcess;
    private readonly ITeammateMailboxService? _fileMailbox;
    [Inject] private readonly ILogger<MailboxHub>? _logger;

    /// <summary>
    /// 创建 MailboxHub。
    /// </summary>
    /// <param name="inProcess">进程内邮箱（内存 Channel）。</param>
    /// <param name="fileMailbox">文件邮箱（跨进程 teammate swarm），null 表示不支持跨进程。</param>
    /// <param name="logger">日志。</param>
    public MailboxHub(
        IMailbox inProcess,
        ITeammateMailboxService? fileMailbox = null,
        ILogger<MailboxHub>? logger = null)
    {
        _inProcess = inProcess ?? throw new ArgumentNullException(nameof(inProcess));
        _fileMailbox = fileMailbox;
        _logger = logger;
    }

    /// <summary>发送消息到指定 agent（按邮箱类型路由）。</summary>
    /// <param name="agentId">接收 agent ID。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="kind">邮箱类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>InProcess 返回是否投递成功；File 返回 true（文件写入不返回 channel 投递结果）。</returns>
    public async Task<bool> SendAsync(string agentId, CoordinatorMessage message, MailboxKind kind, CancellationToken cancellationToken = default)
    {
        return kind switch
        {
            MailboxKind.InProcess => await _inProcess.SendAsync(agentId, message, cancellationToken).ConfigureAwait(false),
            MailboxKind.File => await SendToFileMailboxAsync(agentId, message, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>广播消息（按邮箱类型路由）。</summary>
    /// <param name="message">消息内容。</param>
    /// <param name="kind">邮箱类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task BroadcastAsync(CoordinatorMessage message, MailboxKind kind, CancellationToken cancellationToken = default)
    {
        switch (kind)
        {
            case MailboxKind.InProcess:
                await _inProcess.BroadcastAsync(message, cancellationToken).ConfigureAwait(false);
                break;
            case MailboxKind.File:
                foreach (var agentId in _inProcess.GetRegisteredAgents())
                {
                    if (agentId != message.FromAgentId)
                    {
                        await SendToFileMailboxAsync(agentId, message, cancellationToken).ConfigureAwait(false);
                    }
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <summary>从进程内邮箱接收消息流。</summary>
    /// <param name="agentId">接收 agent ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>消息异步流。</returns>
    public IAsyncEnumerable<CoordinatorMessage> ReceiveAsync(string agentId, CancellationToken cancellationToken = default)
    {
        return _inProcess.ReceiveAsync(agentId, cancellationToken);
    }

    /// <summary>注册 agent 邮箱。</summary>
    /// <param name="agentId">agent ID。</param>
    /// <param name="sessionId">会话 ID（文件邮箱需要）。</param>
    public void RegisterAgent(string agentId, string? sessionId = null)
    {
        _inProcess.RegisterAgent(agentId, sessionId);
    }

    /// <summary>注销 agent 邮箱。</summary>
    /// <param name="agentId">agent ID。</param>
    public void UnregisterAgent(string agentId)
    {
        _inProcess.UnregisterAgent(agentId);
    }

    private async Task<bool> SendToFileMailboxAsync(string agentId, CoordinatorMessage message, CancellationToken cancellationToken)
    {
        if (_fileMailbox is null)
        {
            _logger?.LogWarning("File mailbox not configured, message to {AgentId} dropped", agentId);
            return false;
        }

        var sessionId = _inProcess.GetSessionId(agentId);
        if (string.IsNullOrEmpty(sessionId))
        {
            _logger?.LogWarning("No session ID for agent {AgentId}, cannot send file mailbox message", agentId);
            return false;
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

            await _fileMailbox.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to send file mailbox message to {AgentId}", agentId);
            return false;
        }
    }
}
