namespace Core.Agents.Coordinator;

/// <summary>
/// 网络邮箱 — 通过 <see cref="IPlatformBotAdapter"/> 接入 QQ/飞书等外部消息平台。
/// <para>继承 <see cref="MailboxBase{TMessage}"/>，复用双工+背压+水位线+超时能力。</para>
/// <para>发送：<see cref="HandleSendAsync"/> 本地投递 + 适配器远程投递。</para>
/// <para>接收：后台循环从适配器 <see cref="IPlatformBotAdapter.ReceiveAsync"/> 读取并投递到本地 Agent。</para>
/// <para>平台适配器可插拔替换（QQ/飞书/Discord/自定义），通过构造函数注入。</para>
/// </summary>
public sealed partial class NetworkMailbox : MailboxBase<CoordinatorMessage>
{
    private readonly IPlatformBotAdapter _adapter;
    private readonly ILogger<NetworkMailbox>? _logger;
    private readonly Func<CoordinatorMessage, string>? _targetIdSelector;
    private readonly Func<CoordinatorMessage, string>? _textSelector;
    private Task? _receiveLoopTask;
    private int _disposed;

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
            outputCapacity: 64)
    {
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
    public async ValueTask StartAsync(CancellationToken ct = default)
    {
        await _adapter.StartAsync(ct).ConfigureAwait(false);
        _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(ct), ct);
        _logger?.LogInformation("NetworkMailbox: started on platform {Platform}", _adapter.PlatformName);
    }

    /// <summary>
    /// 发送命令处理 — 本地投递 + 远程平台投递。
    /// </summary>
    protected override async ValueTask HandleSendAsync(string agentId, CoordinatorMessage message, CancellationToken ct)
    {
        DeliverToAgent(agentId, message);

        var targetId = _targetIdSelector?.Invoke(message) ?? agentId;
        var text = _textSelector?.Invoke(message) ?? message.Content ?? string.Empty;
        var platformMsgId = await _adapter.SendAsync(targetId, text, ct).ConfigureAwait(false);
        if (platformMsgId is not null)
        {
            _logger?.LogDebug("NetworkMailbox: sent to {Target} on {Platform}, platformMsgId={Id}",
                targetId, _adapter.PlatformName, platformMsgId);
        }
    }

    /// <summary>
    /// 广播命令处理 — 本地广播 + 远程平台逐个投递。
    /// </summary>
    protected override async ValueTask HandleBroadcastAsync(CoordinatorMessage message, string? excludeAgentId, CancellationToken ct)
    {
        var text = _textSelector?.Invoke(message) ?? message.Content ?? string.Empty;

        foreach (var agentId in GetRegisteredAgents())
        {
            if (agentId == excludeAgentId) continue;
            DeliverToAgent(agentId, message);

            var targetId = _targetIdSelector?.Invoke(message) ?? agentId;
            await _adapter.SendAsync(targetId, text, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 接收循环 — 从适配器读取平台消息，转换为 CoordinatorMessage 投递到本地 Agent。
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var msg in _adapter.ReceiveAsync(ct).ConfigureAwait(false))
            {
                var coordinatorMsg = new CoordinatorMessage
                {
                    FromAgentId = msg.SourceId,
                    ToAgentId = msg.TargetId,
                    MessageType = "text",
                    Content = msg.Text,
                    Timestamp = msg.Timestamp.UtcDateTime
                };
                DeliverToAgent(msg.TargetId, coordinatorMsg);
                _logger?.LogDebug("NetworkMailbox: received from {Source} on {Platform}",
                    msg.SourceId, _adapter.PlatformName);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "NetworkMailbox: receive loop error on {Platform}", _adapter.PlatformName);
        }
    }

    /// <summary>释放资源 — 释放适配器。</summary>
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        if (_receiveLoopTask is not null)
        {
            try { await _receiveLoopTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (TimeoutException) { _logger?.LogWarning("NetworkMailbox: receive loop did not stop within 2s"); }
            catch (OperationCanceledException) { }
        }
        await _adapter.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
