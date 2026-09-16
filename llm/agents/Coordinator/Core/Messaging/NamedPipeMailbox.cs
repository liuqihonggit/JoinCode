namespace Core.Agents.Coordinator;

/// <summary>
/// 有名管道邮箱 — 基于 <see cref="NamedPipeTransport"/> 星型拓扑的跨进程双工邮箱。
/// <para>继承 <see cref="MailboxBase{TMessage}"/>，复用全部双工+背压+水位线+超时能力。</para>
/// <para>跨进程传输：消息序列化为字节，通过 <see cref="NamedPipeTransport"/> 传输到目标进程。</para>
/// <para>本地投递：目标 Agent 在本进程时直接写入 Agent Channel。</para>
/// <para>路由表：AgentId → ProcessId 映射，主机维护全局路由，从机注册时通知主机。</para>
/// <para>序列化：<see cref="MailboxJsonContext"/> AOT 兼容，写入用 JsonSerializer，读取用 RelaxedJsonSerializer 容错。</para>
/// </summary>
[Register(typeof(NamedPipeMailbox), ServiceLifetime.Singleton)]
public sealed partial class NamedPipeMailbox : MailboxBase<CoordinatorMessage>
{
    private readonly NamedPipeTransport _transport;
    private readonly ConcurrentDictionary<string, string> _agentToProcess;
    private readonly ILogger<NamedPipeMailbox>? _logger;
    private readonly CancellationTokenSource _cts;
    private Task? _receiveLoopTask;
    private int _started;

    /// <summary>
    /// 构造有名管道邮箱。
    /// </summary>
    /// <param name="transport">有名管道传输层</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="commandBackpressure">命令通道背压</param>
    /// <param name="agentBackpressure">Agent 消息通道背压</param>
    public NamedPipeMailbox(
        NamedPipeTransport transport,
        ILogger<NamedPipeMailbox>? logger = null,
        ActorBackpressure? commandBackpressure = null,
        ActorBackpressure? agentBackpressure = null)
        : base(
            commandBackpressure ?? ActorBackpressure.CodingAgentTask,
            agentBackpressure ?? MailboxBase<CoordinatorMessage>.DefaultAgentBackpressure,
            outputCapacity: 128)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger;
        _agentToProcess = new ConcurrentDictionary<string, string>();
        _cts = new CancellationTokenSource();
    }

    /// <summary>传输层实例 — 外部可访问用于主机选举订阅。</summary>
    public NamedPipeTransport Transport => _transport;

    /// <summary>当前进程角色（主机/从机）。</summary>
    public ProcessRole Role => _transport.Role;

    /// <summary>
    /// 启动邮箱 — 启动传输层 + 接收循环。
    /// <para>调用方：应用启动时调用一次。</para>
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        await _transport.StartAsync(ct).ConfigureAwait(false);
        _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), ct);
        _logger?.LogInformation("NamedPipeMailbox started (role={Role}, pid={Pid})",
            _transport.Role, _transport.ProcessId);
    }

    /// <summary>
    /// 发送命令处理 — 本地投递 + 跨进程传输。
    /// <para>目标 Agent 在本进程：直接写入 Agent Channel。</para>
    /// <para>目标 Agent 在远程进程：序列化后通过传输层发送。</para>
    /// </summary>
    protected override async ValueTask HandleSendAsync(string agentId, CoordinatorMessage message, CancellationToken ct)
    {
        DeliverToAgent(agentId, message);

        if (_agentToProcess.TryGetValue(agentId, out var targetPid)
            && targetPid != _transport.ProcessId)
        {
            await SendRemoteAsync(targetPid, message, ct).ConfigureAwait(false);
        }
        else if (_transport.Role == ProcessRole.Slave)
        {
            await SendRemoteAsync(_transport.HostProcessId, message, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 广播命令处理 — 本地广播 + 跨进程广播。
    /// </summary>
    protected override async ValueTask HandleBroadcastAsync(CoordinatorMessage message, string? excludeAgentId, CancellationToken ct)
    {
        foreach (var kvp in _agentToProcess)
        {
            if (kvp.Key == excludeAgentId) continue;
            DeliverToAgent(kvp.Key, message);
        }

        try
        {
            var data = JsonSerializer.Serialize(message, MailboxJsonContext.Default.CoordinatorMessage);
            var bytes = Encoding.UTF8.GetBytes(data);
            await _transport.BroadcastAsync(bytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "NamedPipeMailbox: remote broadcast failed");
        }
    }

    /// <summary>
    /// 注册 Agent — 本地注册 + 通知主机路由表更新。
    /// </summary>
    public new ValueTask RegisterAgentAsync(string agentId, string? sessionId = null, CancellationToken ct = default)
    {
        _agentToProcess[agentId] = _transport.ProcessId;
        return base.RegisterAgentAsync(agentId, sessionId, ct);
    }

    /// <summary>
    /// 接收循环 — 从传输层读取跨进程消息，反序列化，投递到本地 Agent Channel。
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _transport.ReceiveAsync(ct).ConfigureAwait(false))
            {
                CoordinatorMessage? message = null;
                try
                {
                    var json = Encoding.UTF8.GetString(frame.Data.Span);
                    message = RelaxedJsonSerializer.Deserialize(json, MailboxJsonContext.Default.CoordinatorMessage);
                }
                catch (JsonException ex)
                {
                    _logger?.LogWarning(ex, "NamedPipeMailbox: failed to deserialize message from {Source}", frame.SourceProcessId);
                    continue;
                }

                if (message is null) continue;

                if (message.ToAgentId is not null)
                {
                    _agentToProcess.TryAdd(message.ToAgentId, frame.SourceProcessId);
                    DeliverToAgent(message.ToAgentId, message);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "NamedPipeMailbox: receive loop error");
        }
    }

    /// <summary>
    /// 序列化消息并通过传输层发送到指定进程。
    /// </summary>
    private async ValueTask SendRemoteAsync(string targetPid, CoordinatorMessage message, CancellationToken ct)
    {
        try
        {
            var data = JsonSerializer.Serialize(message, MailboxJsonContext.Default.CoordinatorMessage);
            var bytes = Encoding.UTF8.GetBytes(data);
            await _transport.SendAsync(targetPid, bytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "NamedPipeMailbox: remote send to {Target} failed", targetPid);
        }
    }

    /// <summary>
    /// 释放邮箱 — 停止接收循环 + 释放传输层。
    /// </summary>
    public override ValueTask DisposeAsync() => DisposeAsyncCore();

    private async ValueTask DisposeAsyncCore()
    {
        _cts.Cancel();
        if (_receiveLoopTask is not null)
        {
            try { await _receiveLoopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        await _transport.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
