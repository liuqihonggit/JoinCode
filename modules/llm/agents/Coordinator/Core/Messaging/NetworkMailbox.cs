namespace Core.Agents.Coordinator;

/// <summary>
/// 网络邮箱 — 通过 <see cref="IPlatformBotAdapter"/> 接入 QQ/飞书等外部消息平台。
/// <para>继承 <see cref="StreamMailboxBase{TMessage, TFrame}"/>，复用双工+背压+水位线+超时+接收循环能力。</para>
/// <para>发送：<see cref="HandleSendAsync"/> 本地投递 + 适配器远程投递。</para>
/// <para>接收：后台循环从适配器 <see cref="IPlatformBotAdapter.ReceiveAsync"/> 读取并投递到本地 Agent。</para>
/// <para>平台适配器可插拔替换（QQ/飞书/Discord/自定义），通过构造函数注入。</para>
/// </summary>
public sealed partial class NetworkMailbox : StreamMailboxBase<CoordinatorMessage, PlatformMessage> {
    private readonly IPlatformBotAdapter _adapter;
    private readonly ILogger<NetworkMailbox>? _logger;
    private readonly Func<CoordinatorMessage, string>? _targetIdSelector;
    private readonly Func<CoordinatorMessage, string>? _textSelector;

    /// <summary>
    /// 构造网络邮箱 — 注入平台适配器。
    /// </summary>
    /// <param name="adapter">平台机器人适配器（QQ/飞书/Discord 等）</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="targetIdSelector">从消息中提取目标平台ID的函数（默认用消息的 ToAgentId）</param>
    /// <param name="textSelector">从消息中提取文本的函数（默认用消息的 Content）</param>
    /// <param name="commandBackpressure">命令通道背压</param>
    /// <param name="agentBackpressure">Agent 消息通道背压</param>
    public NetworkMailbox(
        IPlatformBotAdapter adapter,
        ILogger<NetworkMailbox>? logger = null,
        Func<CoordinatorMessage, string>? targetIdSelector = null,
        Func<CoordinatorMessage, string>? textSelector = null,
        ActorBackpressure? commandBackpressure = null,
        ActorBackpressure? agentBackpressure = null)
        : base(
            commandBackpressure ?? ActorBackpressure.CodingAgentTask,
            agentBackpressure ?? DefaultAgentBackpressure,
            outputCapacity: 64) {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _logger = logger;
        _targetIdSelector = targetIdSelector;
        _textSelector = textSelector;
    }

    /// <summary>平台名称 — 日志标识。</summary>
    public string PlatformName => _adapter.PlatformName;

    /// <summary>适配器是否已连接。</summary>
    public bool IsConnected => _adapter.IsConnected;

    /// <summary>
    /// 启动网络邮箱 — 启动适配器 + 启动接收循环。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async ValueTask StartAsync(CancellationToken ct = default) {
        await _adapter.StartAsync(ct).ConfigureAwait(false);
        StartReceiveLoop();
        _logger?.LogInformation("NetworkMailbox: started on platform {Platform}", _adapter.PlatformName);
    }

    /// <summary>
    /// 发送命令处理 — 本地投递 + 远程平台投递。
    /// </summary>
    protected override async ValueTask HandleSendAsync(string agentId, CoordinatorMessage message, CancellationToken ct) {
        DeliverToAgent(agentId, message);

        var targetId = _targetIdSelector?.Invoke(message) ?? agentId;
        var text = _textSelector?.Invoke(message) ?? message.Content ?? string.Empty;
        var platformMsgId = await _adapter.SendAsync(targetId, text, ct).ConfigureAwait(false);
        if (platformMsgId is not null) {
            _logger?.LogDebug("NetworkMailbox: sent to {Target} on {Platform}, platformMsgId={Id}",
                targetId, _adapter.PlatformName, platformMsgId);
        }
    }

    /// <summary>
    /// 广播命令处理 — 本地广播 + 远程平台逐个投递。
    /// </summary>
    protected override async ValueTask HandleBroadcastAsync(CoordinatorMessage message, string? excludeAgentId, CancellationToken ct) {
        var text = _textSelector?.Invoke(message) ?? message.Content ?? string.Empty;

        foreach (var agentId in GetRegisteredAgents()) {
            if (agentId == excludeAgentId) continue;
            DeliverToAgent(agentId, message);

            var targetId = _targetIdSelector?.Invoke(message) ?? agentId;
            await _adapter.SendAsync(targetId, text, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 从适配器读取平台消息序列 — 接收循环骨架调用。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>平台消息异步枚举流</returns>
    protected override IAsyncEnumerable<PlatformMessage> ReceiveFramesAsync(CancellationToken ct)
        => _adapter.ReceiveAsync(ct);

    /// <summary>
    /// 处理单帧 — 平台消息转换为 CoordinatorMessage + 投递到本地 Agent Channel。
    /// </summary>
    /// <param name="frame">平台消息帧</param>
    /// <param name="ct">取消令牌</param>
    protected override ValueTask HandleFrameAsync(PlatformMessage frame, CancellationToken ct) {
        var coordinatorMsg = new CoordinatorMessage {
            FromAgentId = frame.SourceId,
            ToAgentId = frame.TargetId,
            MessageType = "text",
            Content = frame.Text,
            Timestamp = frame.Timestamp.UtcDateTime
        };
        DeliverToAgent(frame.TargetId, coordinatorMsg);
        _logger?.LogDebug("NetworkMailbox: received from {Source} on {Platform}",
            frame.SourceId, _adapter.PlatformName);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 日志接收循环错误 — 非取消异常。
    /// </summary>
    /// <param name="ex">异常</param>
    protected override void LogReceiveLoopError(Exception ex)
        => _logger?.LogError(ex, "NetworkMailbox: receive loop error on {Platform}", _adapter.PlatformName);

    /// <summary>
    /// 释放资源 — 停止接收循环 + 释放适配器。
    /// </summary>
    public override async ValueTask DisposeAsync() {
        await StopReceiveLoopAsync().ConfigureAwait(false);
        await _adapter.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}