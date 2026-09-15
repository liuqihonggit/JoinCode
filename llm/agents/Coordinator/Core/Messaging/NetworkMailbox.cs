namespace Core.Agents.Coordinator;

/// <summary>
/// 网络邮箱 — 预留接口，日后接入 QQ/飞书等外部消息平台。
/// <para>继承 <see cref="MailboxBase{TMessage}"/>，复用双工+背压+水位线+超时能力。</para>
/// <para>当前为骨架实现，具体传输层（HttpClient/WebSocket/QQ Bot API/飞书 Bot API）待接入。</para>
/// <para>接入点：重写 <see cref="HandleSendAsync"/> 实现远程投递，重写 <see cref="HandleBroadcastAsync"/> 实现远程广播。</para>
/// </summary>
public sealed partial class NetworkMailbox : MailboxBase<CoordinatorMessage>
{
    private readonly ILogger<NetworkMailbox>? _logger;
    private readonly string _platformName;

    /// <summary>
    /// 构造网络邮箱 — 预留平台接入点。
    /// </summary>
    /// <param name="platformName">平台名称（如 "qq"/"feishu"/"websocket"），用于日志标识</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="commandBackpressure">命令通道背压</param>
    /// <param name="agentBackpressure">Agent 消息通道背压</param>
    public NetworkMailbox(
        string platformName = "network",
        ILogger<NetworkMailbox>? logger = null,
        ActorBackpressure? commandBackpressure = null,
        ActorBackpressure? agentBackpressure = null)
        : base(
            commandBackpressure ?? ActorBackpressure.CodingAgentTask,
            agentBackpressure ?? DefaultAgentBackpressure,
            outputCapacity: 64)
    {
        _platformName = platformName;
        _logger = logger;
    }

    /// <summary>平台名称 — 日志标识。</summary>
    public string PlatformName => _platformName;

    /// <summary>
    /// 发送命令处理 — 本地投递 + 预留远程投递点。
    /// <para>TODO: 接入具体平台 API（QQ Bot/飞书 Bot/WebSocket）时重写此方法。</para>
    /// </summary>
    protected override ValueTask HandleSendAsync(string agentId, CoordinatorMessage message, CancellationToken ct)
    {
        DeliverToAgent(agentId, message);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 广播命令处理 — 本地广播 + 预留远程广播点。
    /// <para>TODO: 接入具体平台 API 时重写此方法实现远程广播。</para>
    /// </summary>
    protected override ValueTask HandleBroadcastAsync(CoordinatorMessage message, string? excludeAgentId, CancellationToken ct)
    {
        foreach (var agentId in GetRegisteredAgents())
        {
            if (agentId != excludeAgentId)
            {
                DeliverToAgent(agentId, message);
            }
        }
        return ValueTask.CompletedTask;
    }
}
