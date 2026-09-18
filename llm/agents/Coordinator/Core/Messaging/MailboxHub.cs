namespace Core.Agents.Coordinator;

/// <summary>
/// 邮箱中枢 — 统一消息传递入口，按 MailboxKind 路由到四种通道 — ADR 0111。
/// <para>通道：InProcess（内存）/ File（JSONL 持久化）/ NamedPipe（跨进程双工）/ Network（QQ/飞书）。</para>
/// <para>NamedPipe/Network 通道通过 <see cref="RegisterChannel"/> 运行时注册，InProcess/File 通过构造函数注入。</para>
/// <para>自动路由：<see cref="SendAsync(string, CoordinatorMessage, CancellationToken)"/> 按 agent 注册的通道路由。</para>
/// <para>跨通道广播：<see cref="BroadcastAsync(CoordinatorMessage, CancellationToken)"/> 向所有已注册通道广播。</para>
/// </summary>
[Register(typeof(MailboxHub), ServiceLifetime.Singleton)]
public sealed partial class MailboxHub
{
    private readonly IMailbox _inProcess;
    private readonly ITeammateMailboxService? _fileMailbox;
    private readonly ConcurrentDictionary<MailboxKind, MailboxBase<CoordinatorMessage>> _extraChannels;
    private readonly AgentChannelRegistry _agentChannels;
    private readonly ILogger<MailboxHub>? _logger;

    /// <summary>
    /// 创建 MailboxHub — DI 友好构造函数。
    /// </summary>
    /// <param name="inProcess">进程内邮箱（内存 Channel），必需。</param>
    /// <param name="fileMailbox">文件邮箱（跨进程 teammate swarm），null 表示不支持文件通道。</param>
    /// <param name="logger">日志记录器。</param>
    public MailboxHub(
        IMailbox inProcess,
        ITeammateMailboxService? fileMailbox = null,
        ILogger<MailboxHub>? logger = null)
    {
        _inProcess = inProcess ?? throw new ArgumentNullException(nameof(inProcess));
        _fileMailbox = fileMailbox;
        _extraChannels = new ConcurrentDictionary<MailboxKind, MailboxBase<CoordinatorMessage>>();
        _agentChannels = new AgentChannelRegistry();
        _logger = logger;
    }

    /// <summary>
    /// 注册额外通道（NamedPipe/Network）— 运行时动态注册。
    /// <para>InProcess 通道构造函数注入，不可覆盖。File 通道走 <c>ITeammateMailboxService</c> 遗留路由。</para>
    /// </summary>
    /// <param name="kind">通道类型（必须是 NamedPipe 或 Network）。</param>
    /// <param name="mailbox">邮箱实例（MailboxBase 子类）。</param>
    public void RegisterChannel(MailboxKind kind, MailboxBase<CoordinatorMessage> mailbox)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        if (kind is MailboxKind.InProcess or MailboxKind.File)
            throw new ArgumentException($"Channel {kind} is managed by constructor, use RegisterChannel only for NamedPipe/Network", nameof(kind));
        _extraChannels[kind] = mailbox;
        _logger?.LogInformation("MailboxHub: registered channel {Kind}", kind);
    }

    /// <summary>指定通道是否已注册。</summary>
    public bool IsChannelAvailable(MailboxKind kind)
        => kind switch
        {
            MailboxKind.InProcess => true,
            MailboxKind.File => _fileMailbox is not null,
            MailboxKind.NamedPipe => _extraChannels.ContainsKey(MailboxKind.NamedPipe),
            MailboxKind.Network => _extraChannels.ContainsKey(MailboxKind.Network),
            _ => false
        };

    /// <summary>
    /// 发送消息到指定 agent（显式通道）。
    /// </summary>
    /// <param name="agentId">接收 agent ID。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="kind">通道类型。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true=已投递；false=通道未注册或投递失败。</returns>
    public async ValueTask<bool> SendAsync(string agentId, CoordinatorMessage message, MailboxKind kind, CancellationToken ct = default)
    {
        return kind switch
        {
            MailboxKind.InProcess => await _inProcess.SendAsync(agentId, message, ct).ConfigureAwait(false),
            MailboxKind.File => await SendToFileMailboxAsync(agentId, message, ct).ConfigureAwait(false),
            MailboxKind.NamedPipe => await SendToExtraChannelAsync(MailboxKind.NamedPipe, agentId, message, ct).ConfigureAwait(false),
            MailboxKind.Network => await SendToExtraChannelAsync(MailboxKind.Network, agentId, message, ct).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>
    /// 发送消息到指定 agent（自动路由 — 按 agent 注册的通道路由，默认 InProcess）。
    /// </summary>
    /// <param name="agentId">接收 agent ID。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true=已投递；false=通道未注册或投递失败。</returns>
    public ValueTask<bool> SendAsync(string agentId, CoordinatorMessage message, CancellationToken ct = default)
    {
        var kind = _agentChannels.GetChannel(agentId);
        return SendAsync(agentId, message, kind, ct);
    }

    /// <summary>
    /// 广播消息到指定通道。
    /// </summary>
    /// <param name="message">消息内容。</param>
    /// <param name="kind">通道类型。</param>
    /// <param name="ct">取消令牌。</param>
    public async ValueTask BroadcastAsync(CoordinatorMessage message, MailboxKind kind, CancellationToken ct = default)
    {
        switch (kind)
        {
            case MailboxKind.InProcess:
                await _inProcess.BroadcastAsync(message, ct).ConfigureAwait(false);
                break;
            case MailboxKind.File:
                foreach (var agentId in _inProcess.GetRegisteredAgents())
                {
                    if (agentId != message.FromAgentId)
                        await SendToFileMailboxAsync(agentId, message, ct).ConfigureAwait(false);
                }
                break;
            case MailboxKind.NamedPipe:
                await BroadcastToExtraChannelAsync(MailboxKind.NamedPipe, message, ct).ConfigureAwait(false);
                break;
            case MailboxKind.Network:
                await BroadcastToExtraChannelAsync(MailboxKind.Network, message, ct).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <summary>
    /// 跨所有已注册通道广播消息 — 聊天室广播语义。
    /// <para>InProcess + File + NamedPipe + Network 逐通道广播，缺失通道跳过。</para>
    /// </summary>
    /// <param name="message">消息内容。</param>
    /// <param name="ct">取消令牌。</param>
    public async ValueTask BroadcastAsync(CoordinatorMessage message, CancellationToken ct = default)
    {
        await _inProcess.BroadcastAsync(message, ct).ConfigureAwait(false);

        if (_fileMailbox is not null)
        {
            foreach (var agentId in _inProcess.GetRegisteredAgents())
            {
                if (agentId != message.FromAgentId)
                    await SendToFileMailboxAsync(agentId, message, ct).ConfigureAwait(false);
            }
        }

        foreach (var (kind, mailbox) in _extraChannels)
        {
            try
            {
                await mailbox.TellBroadcastAsync(message, message.FromAgentId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "MailboxHub: broadcast failed on {Kind}", kind);
            }
        }
    }

    /// <summary>
    /// 按可见性广播消息 — ADR 0111 决策7。
    /// <para>Public/System → 广播所有已注册通道</para>
    /// <para>AdminOnly → 仅投递给 Role &lt;= Admin 的 agent</para>
    /// <para>Private → 仅投递给 ToAgentId</para>
    /// <para>Hidden → 不投递</para>
    /// </summary>
    /// <param name="message">消息内容（Visibility 字段决定投递范围）。</param>
    /// <param name="visibility">消息可见性（覆盖 message.Visibility，显式控制投递范围）。</param>
    /// <param name="ct">取消令牌。</param>
    public async ValueTask BroadcastAsync(CoordinatorMessage message, MessageVisibility visibility, CancellationToken ct = default)
    {
        switch (visibility)
        {
            case MessageVisibility.Hidden:
                _logger?.LogDebug("MailboxHub: hidden message {MessageId} not delivered", message.MessageId);
                return;

            case MessageVisibility.Private:
                if (string.IsNullOrEmpty(message.ToAgentId))
                {
                    _logger?.LogWarning("MailboxHub: private message {MessageId} has no ToAgentId, dropped", message.MessageId);
                    return;
                }
                await SendAsync(message.ToAgentId, message, ct).ConfigureAwait(false);
                return;

            case MessageVisibility.AdminOnly:
                foreach (var (agentId, role) in _agentChannels.GetAllRoles())
                {
                    if (role > ChatRoomRole.Admin) continue;
                    if (agentId == message.FromAgentId) continue;
                    await SendAsync(agentId, message, ct).ConfigureAwait(false);
                }
                return;

            case MessageVisibility.Public:
            case MessageVisibility.System:
            default:
                await BroadcastAsync(message, ct).ConfigureAwait(false);
                return;
        }
    }

    /// <summary>获取 agent 的聊天室角色 — ADR 0111 决策7。</summary>
    public ChatRoomRole GetAgentRole(string agentId)
        => _agentChannels.GetRole(agentId);

    /// <summary>
    /// 从 agent 所在通道接收消息流。
    /// </summary>
    /// <param name="agentId">接收 agent ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>消息异步流；通道未注册时返回空流。</returns>
    public IAsyncEnumerable<CoordinatorMessage> ReceiveAsync(string agentId, CancellationToken ct = default)
    {
        var kind = _agentChannels.GetChannel(agentId);
        return kind switch
        {
            MailboxKind.InProcess => _inProcess.ReceiveAsync(agentId, ct),
            MailboxKind.NamedPipe when _extraChannels.TryGetValue(MailboxKind.NamedPipe, out var mb)
                => mb.ReceiveAsync(agentId, ct),
            MailboxKind.Network when _extraChannels.TryGetValue(MailboxKind.Network, out var mb)
                => mb.ReceiveAsync(agentId, ct),
            _ => AsyncEnumerable.Empty<CoordinatorMessage>()
        };
    }

    /// <summary>
    /// 注册 agent 到指定通道 — 绑定 agent 与通道偏好。
    /// </summary>
    /// <param name="agentId">agent ID。</param>
    /// <param name="kind">通道类型（默认 InProcess）。</param>
    /// <param name="sessionId">会话 ID（文件邮箱需要）。</param>
    /// <param name="role">聊天室角色（默认 Member）— ADR 0111 决策7，用于 AdminOnly 可见性过滤。</param>
    /// <param name="ct">取消令牌。</param>
    public async ValueTask RegisterAgentAsync(string agentId, MailboxKind kind = MailboxKind.InProcess, string? sessionId = null, ChatRoomRole role = ChatRoomRole.Member, CancellationToken ct = default)
    {
        _agentChannels.Register(agentId, kind, role);

        switch (kind)
        {
            case MailboxKind.InProcess:
                _inProcess.RegisterAgent(agentId, sessionId);
                break;
            case MailboxKind.NamedPipe when _extraChannels.TryGetValue(MailboxKind.NamedPipe, out var mb):
                await mb.RegisterAgentAsync(agentId, sessionId, ct).ConfigureAwait(false);
                break;
            case MailboxKind.Network when _extraChannels.TryGetValue(MailboxKind.Network, out var mb):
                await mb.RegisterAgentAsync(agentId, sessionId, ct).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// 注册 agent 到进程内邮箱（遗留同步重载 — InProcess 通道，同步注册）。
    /// </summary>
    public void RegisterAgent(string agentId, string? sessionId = null)
    {
        _agentChannels.SetChannel(agentId, MailboxKind.InProcess);
        _inProcess.RegisterAgent(agentId, sessionId);
    }

    /// <summary>
    /// 注销 agent 邮箱 — 从其注册的通道移除。
    /// </summary>
    /// <param name="agentId">agent ID。</param>
    /// <param name="ct">取消令牌。</param>
    public async ValueTask UnregisterAgentAsync(string agentId, CancellationToken ct = default)
    {
        _agentChannels.Unregister(agentId, out var kind);
        switch (kind)
        {
            case MailboxKind.InProcess:
                _inProcess.UnregisterAgent(agentId);
                break;
            case MailboxKind.NamedPipe when _extraChannels.TryGetValue(MailboxKind.NamedPipe, out var mb):
                await mb.UnregisterAgentAsync(agentId, ct).ConfigureAwait(false);
                break;
            case MailboxKind.Network when _extraChannels.TryGetValue(MailboxKind.Network, out var mb):
                await mb.UnregisterAgentAsync(agentId, ct).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// 注销 agent 邮箱（遗留同步重载 — InProcess 通道，同步注销）。
    /// </summary>
    public void UnregisterAgent(string agentId)
    {
        _agentChannels.Unregister(agentId, out _);
        _inProcess.UnregisterAgent(agentId);
    }

    /// <summary>获取所有已注册 agent — 跨通道聚合。</summary>
    public IEnumerable<string> GetRegisteredAgents()
    {
        foreach (var agentId in _inProcess.GetRegisteredAgents())
            yield return agentId;
        foreach (var (_, mailbox) in _extraChannels)
            foreach (var agentId in mailbox.GetRegisteredAgents())
                if (!_inProcess.GetRegisteredAgents().Contains(agentId))
                    yield return agentId;
    }

    /// <summary>获取 agent 的会话 ID（从进程内邮箱查询）。</summary>
    public string? GetSessionId(string agentId) => _inProcess.GetSessionId(agentId);

    /// <summary>获取 agent 注册的通道类型。</summary>
    public MailboxKind GetAgentChannel(string agentId)
        => _agentChannels.GetChannel(agentId);

    private async ValueTask<bool> SendToFileMailboxAsync(string agentId, CoordinatorMessage message, CancellationToken ct)
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

            await _fileMailbox.SendAsync(request, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to send file mailbox message to {AgentId}", agentId);
            return false;
        }
    }

    private async ValueTask<bool> SendToExtraChannelAsync(MailboxKind kind, string agentId, CoordinatorMessage message, CancellationToken ct)
    {
        if (!_extraChannels.TryGetValue(kind, out var mailbox))
        {
            _logger?.LogWarning("Channel {Kind} not registered, message to {AgentId} dropped", kind, agentId);
            return false;
        }

        try
        {
            await mailbox.TellAsync(agentId, message, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to send {Kind} message to {AgentId}", kind, agentId);
            return false;
        }
    }

    private async ValueTask BroadcastToExtraChannelAsync(MailboxKind kind, CoordinatorMessage message, CancellationToken ct)
    {
        if (!_extraChannels.TryGetValue(kind, out var mailbox))
        {
            _logger?.LogWarning("Channel {Kind} not registered, broadcast dropped", kind);
            return;
        }

        try
        {
            await mailbox.TellBroadcastAsync(message, message.FromAgentId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to broadcast on {Kind}", kind);
        }
    }
}
