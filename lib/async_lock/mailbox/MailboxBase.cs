namespace Core.Utils;

/// <summary>
/// Agent 邮箱条目 — 合并 Agent 的 Channel 与 SessionId 为单一数据结构，按 agentId 索引。
/// <para>消除 <c>MailboxBase</c> 中两个并行字典（<c>_agentChannels</c> + <c>_agentSessions</c>）的同步开销与一致性风险。</para>
/// </summary>
/// <typeparam name="TMessage">消息类型</typeparam>
/// <param name="Channel">Agent 的有界消息通道。</param>
/// <param name="SessionId">可选会话 ID（跨进程邮箱用于路由），null 表示未关联会话。</param>
public sealed record AgentMailboxEntry<TMessage>(
    Channel<TMessage> Channel,
    string? SessionId = null);

/// <summary>
/// 邮箱命令基类 — 由 MailboxBase 的 Consumer 线程串行处理，路由表无需锁。
/// </summary>
/// <typeparam name="TMessage">消息类型</typeparam>
public abstract record MailboxCmd<TMessage>;

/// <summary>投递消息到指定 Agent 邮箱。</summary>
public sealed record SendCmd<TMessage>(string AgentId, TMessage Message) : MailboxCmd<TMessage>;

/// <summary>广播消息到所有已注册邮箱（可排除发送者）。</summary>
public sealed record BroadcastCmd<TMessage>(TMessage Message, string? ExcludeAgentId = null) : MailboxCmd<TMessage>;

/// <summary>注册 Agent 邮箱，创建对应的有界 Channel。</summary>
public sealed record RegisterAgentCmd<TMessage>(string AgentId, string? SessionId = null) : MailboxCmd<TMessage>;

/// <summary>注销 Agent 邮箱，完成对应 Channel 并移除映射。</summary>
public sealed record UnregisterAgentCmd<TMessage>(string AgentId) : MailboxCmd<TMessage>;

/// <summary>
/// 排空屏障命令 — 入队后等 Consumer 处理到此命令，FIFO 保证之前所有命令已处理完。
/// <para>用于确定性等待（替代 Task.Delay 固定等待 fire-and-forget 入队后的异步副作用）。</para>
/// <para>生产用途：优雅关闭前确保命令处理完、批量操作后确认生效。测试用途：替代 Task.Delay。</para>
/// </summary>
public sealed record DrainBarrierCmd<TMessage>(TaskCompletionSource Tcs) : MailboxCmd<TMessage>;

/// <summary>
/// 邮箱事件基类 — 通过 OutputAsync 输出，外部可订阅监控。
/// </summary>
/// <typeparam name="TMessage">消息类型</typeparam>
public abstract record MailboxEvt<TMessage>;

/// <summary>水位线告警事件 — Agent 邮箱达到高/危险水位，生产方应限速。</summary>
public sealed record WatermarkReachedEvt<TMessage>(
    string AgentId,
    WatermarkLevel Level,
    int CurrentCount,
    int Capacity) : MailboxEvt<TMessage>;

/// <summary>Agent 注册成功事件。</summary>
public sealed record AgentRegisteredEvt<TMessage>(string AgentId) : MailboxEvt<TMessage>;

/// <summary>Agent 注销事件。</summary>
public sealed record AgentUnregisteredEvt<TMessage>(string AgentId) : MailboxEvt<TMessage>;

/// <summary>
/// 统一邮箱基类 — 四种邮箱（进程内/有名管道/文件/网络）的共同基类。
/// <para>继承 <see cref="ActorBase{TCommand, TOut}"/>，复用全部双工+背压+水位线+超时能力。</para>
/// <para>命令通道（输入）：Send/Broadcast/Register/Unregister 命令，有界+水位线+超时。</para>
/// <para>事件通道（输出）：WatermarkReached/AgentRegistered/AgentUnregistered 事件，供外部监控。</para>
/// <para>路由表：<see cref="ConcurrentDictionary{TKey, TValue}"/> 管理 Agent → Channel 映射，每个 Agent 独立有界 Channel。</para>
/// <para>tell 异步：<see cref="TellAsync"/> 只入命令通道不等待响应（fire-and-forget），禁止 ask 阻塞。</para>
/// <para>水位线限速：Agent Channel 达到高水位线时触发 <see cref="WatermarkReachedEvt{TMessage}"/>，生产方应降速。</para>
/// <para>子类重写 <see cref="HandleSendAsync"/>/<see cref="HandleBroadcastAsync"/> 实现跨进程传输（有名管道/文件/网络）。</para>
/// </summary>
/// <typeparam name="TMessage">消息类型 — 建议用 sealed class 或 record</typeparam>
public abstract class MailboxBase<TMessage> : ActorBase<MailboxCmd<TMessage>, MailboxEvt<TMessage>> {
    private readonly ConcurrentDictionary<string, AgentMailboxEntry<TMessage>> _agentMailboxes;
    private readonly ActorBackpressure _agentBackpressure;

    /// <summary>
    /// 构造邮箱基类。
    /// </summary>
    /// <param name="commandBackpressure">命令通道背压（null=无界，建议用 <see cref="ActorBackpressure.CodingAgentTask"/>）</param>
    /// <param name="agentBackpressure">Agent 消息通道背压（null=默认 256 容量+200 高水位+240 危险水位+10s 超时）</param>
    /// <param name="outputCapacity">事件输出通道容量（null=无界）</param>
    protected MailboxBase(
        ActorBackpressure? commandBackpressure = null,
        ActorBackpressure? agentBackpressure = null,
        int? outputCapacity = null)
        : base(commandBackpressure, outputCapacity) {
        _agentMailboxes = new ConcurrentDictionary<string, AgentMailboxEntry<TMessage>>();
        _agentBackpressure = agentBackpressure ?? DefaultAgentBackpressure;
    }

    /// <summary>默认 Agent 通道背压 — 256 容量 + Wait + 200 高水位 + 240 危险水位 + 10s 超时。</summary>
    public static readonly ActorBackpressure DefaultAgentBackpressure = new(
        Capacity: 256,
        FullMode: BoundedChannelFullMode.Wait,
        HighWatermark: 200,
        CriticalWatermark: 240,
        SendTimeout: TimeSpan.FromSeconds(10));

    /// <summary>
    /// tell 异步发送 — 只入命令通道，不等待响应（fire-and-forget）。
    /// <para>命令由 Consumer 线程串行处理，投递到目标 Agent 的有界 Channel。</para>
    /// <para>命令通道满时背压等待（配置了 commandBackpressure 时）。</para>
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="message">消息内容</param>
    /// <param name="ct">取消令牌</param>
    public ValueTask TellAsync(string agentId, TMessage message, CancellationToken ct = default)
        => SendAsync(new SendCmd<TMessage>(agentId, message), ct);

    /// <summary>
    /// tell 异步广播 — 投递到所有已注册 Agent（可排除发送者）。
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <param name="excludeAgentId">排除的 Agent（通常为发送者），null 表示不排除</param>
    /// <param name="ct">取消令牌</param>
    public ValueTask TellBroadcastAsync(TMessage message, string? excludeAgentId = null, CancellationToken ct = default)
        => SendAsync(new BroadcastCmd<TMessage>(message, excludeAgentId), ct);

    /// <summary>
    /// 等待所有已入队命令被 Consumer 处理完 — 入队屏障命令并等其处理（FIFO 保证之前的命令都已完成）。
    /// <para>用于确定性等待，替代 <c>Task.Delay</c> 固定等待：Tell 系列方法 fire-and-forget 入队后 Consumer 异步处理，</para>
    /// <para>固定等待在 CI 高负载时不可靠（Consumer 未在时限内调度完 → 副作用未生效 → 断言失败）。</para>
    /// <para>生产用途：优雅关闭前确保命令处理完、批量操作后确认生效。测试用途：替代 Task.Delay 等异步副作用。</para>
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task WaitForCommandsDrainedAsync(CancellationToken ct = default) {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync(new DrainBarrierCmd<TMessage>(tcs), ct).ConfigureAwait(false);
        await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 注册 Agent 邮箱 — 创建对应的有界 Channel。
    /// </summary>
    /// <param name="agentId">Agent 标识</param>
    /// <param name="sessionId">可选会话 ID（跨进程邮箱用于路由）</param>
    /// <param name="ct">取消令牌</param>
    public ValueTask RegisterAgentAsync(string agentId, string? sessionId = null, CancellationToken ct = default)
        => SendAsync(new RegisterAgentCmd<TMessage>(agentId, sessionId), ct);

    /// <summary>
    /// 注销 Agent 邮箱 — 完成对应 Channel 并移除映射。
    /// </summary>
    /// <param name="agentId">Agent 标识</param>
    /// <param name="ct">取消令牌</param>
    public ValueTask UnregisterAgentAsync(string agentId, CancellationToken ct = default)
        => SendAsync(new UnregisterAgentCmd<TMessage>(agentId), ct);

    /// <summary>
    /// 接收指定 Agent 的消息流 — 直接读 Agent 的有界 Channel，零中间层。
    /// <para>未注册时返回空流。</para>
    /// </summary>
    /// <param name="agentId">Agent 标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>消息异步枚举流</returns>
    public IAsyncEnumerable<TMessage> ReceiveAsync(string agentId, CancellationToken ct = default) {
        if (_agentMailboxes.TryGetValue(agentId, out var entry)) {
            return entry.Channel.Reader.ReadAllAsync(ct);
        }
        return AsyncEnumerable.Empty<TMessage>();
    }

    /// <summary>获取所有已注册 Agent 标识 — 零拷贝键视图。</summary>
    public IEnumerable<string> GetRegisteredAgents() => _agentMailboxes.Keys;

    /// <summary>获取指定 Agent 关联的会话 ID。</summary>
    /// <param name="agentId">Agent 标识</param>
    /// <returns>会话 ID；未关联时返回 null</returns>
    public string? GetSessionId(string agentId) => _agentMailboxes.GetValueOrDefault(agentId)?.SessionId;

    /// <summary>获取指定 Agent 通道的当前消息数（无界通道返回 0）。</summary>
    public int GetAgentMessageCount(string agentId)
        => _agentMailboxes.TryGetValue(agentId, out var e) && e.Channel.Reader.CanCount ? e.Channel.Reader.Count : 0;

    /// <summary>指定 Agent 是否达到高水位线（生产方应限速）。</summary>
    public bool IsAgentHighWatermark(string agentId)
        => _agentMailboxes.TryGetValue(agentId, out var e)
           && e.Channel.Reader.CanCount
           && e.Channel.Reader.Count >= _agentBackpressure.EffectiveHighWatermark;

    /// <summary>指定 Agent 是否达到危险水位线（即将满）。</summary>
    public bool IsAgentCriticalWatermark(string agentId)
        => _agentMailboxes.TryGetValue(agentId, out var e)
           && e.Channel.Reader.CanCount
           && e.Channel.Reader.Count >= _agentBackpressure.EffectiveCriticalWatermark;

    /// <summary>Agent 通道背压配置 — 子类和外部可读取用于监控。</summary>
    public ActorBackpressure AgentBackpressure => _agentBackpressure;

    /// <summary>
    /// 命令处理 — Consumer 线程独占执行，路由表无需锁。
    /// </summary>
    /// <param name="cmd">邮箱命令</param>
    /// <param name="ct">取消令牌</param>
    protected override async ValueTask HandleAsync(MailboxCmd<TMessage> cmd, CancellationToken ct) {
        switch (cmd) {
            case SendCmd<TMessage> send:
            await HandleSendAsync(send.AgentId, send.Message, ct).ConfigureAwait(false);
            break;
            case BroadcastCmd<TMessage> broadcast:
            await HandleBroadcastAsync(broadcast.Message, broadcast.ExcludeAgentId, ct).ConfigureAwait(false);
            break;
            case RegisterAgentCmd<TMessage> register:
            HandleRegisterAgent(register.AgentId, register.SessionId);
            break;
            case UnregisterAgentCmd<TMessage> unregister:
            HandleUnregisterAgent(unregister.AgentId);
            break;
            case DrainBarrierCmd<TMessage> barrier:
            barrier.Tcs.TrySetResult();
            break;
            default:
            throw new InvalidOperationException($"Unknown mailbox command: {cmd?.GetType().Name}");
        }
    }

    /// <summary>
    /// 发送命令处理 — 子类重写以实现跨进程传输（有名管道/文件/网络）。
    /// <para>默认实现：直接投递到本地 Agent Channel。</para>
    /// </summary>
    /// <param name="agentId">目标 Agent</param>
    /// <param name="message">消息</param>
    /// <param name="ct">取消令牌</param>
    protected virtual ValueTask HandleSendAsync(string agentId, TMessage message, CancellationToken ct) {
        DeliverToAgent(agentId, message);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 广播命令处理 — 子类重写以实现跨进程广播。
    /// <para>默认实现：遍历本地所有 Agent Channel 投递。</para>
    /// </summary>
    /// <param name="message">消息</param>
    /// <param name="excludeAgentId">排除的 Agent</param>
    /// <param name="ct">取消令牌</param>
    protected virtual ValueTask HandleBroadcastAsync(TMessage message, string? excludeAgentId, CancellationToken ct) {
        foreach (var kvp in _agentMailboxes) {
            if (kvp.Key != excludeAgentId) {
                DeliverToAgent(kvp.Key, message);
            }
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 投递消息到本地 Agent Channel — 子类调用以写入本地邮箱。
    /// <para>Agent 未注册时静默丢弃（子类可重写 <see cref="HandleSendAsync"/> 做跨进程投递）。</para>
    /// </summary>
    /// <param name="agentId">目标 Agent</param>
    /// <param name="message">消息</param>
    protected void DeliverToAgent(string agentId, TMessage message) {
        if (_agentMailboxes.TryGetValue(agentId, out var entry)) {
            if (entry.Channel.Writer.TryWrite(message)) {
                CheckAgentWatermark(agentId, entry.Channel);
            }
        }
    }

    /// <summary>获取或创建 Agent Channel — 子类可用于直接访问通道。</summary>
    protected Channel<TMessage>? GetAgentChannel(string agentId)
        => _agentMailboxes.GetValueOrDefault(agentId)?.Channel;

    private void HandleRegisterAgent(string agentId, string? sessionId) {
        if (_agentMailboxes.ContainsKey(agentId)) return;

        var channel = Channel.CreateBounded<TMessage>(new BoundedChannelOptions(_agentBackpressure.Capacity) {
            FullMode = _agentBackpressure.FullMode,
            SingleReader = true,
            SingleWriter = false
        });

        _agentMailboxes[agentId] = new AgentMailboxEntry<TMessage>(channel, sessionId);

        TryPublish(new AgentRegisteredEvt<TMessage>(agentId));
    }

    private void HandleUnregisterAgent(string agentId) {
        if (_agentMailboxes.TryRemove(agentId, out var entry)) {
            entry.Channel.Writer.TryComplete();
            TryPublish(new AgentUnregisteredEvt<TMessage>(agentId));
        }
    }

    private void CheckAgentWatermark(string agentId, Channel<TMessage> channel) {
        if (!channel.Reader.CanCount) return;
        var count = channel.Reader.Count;
        var level = count >= _agentBackpressure.EffectiveCriticalWatermark ? WatermarkLevel.Critical
                   : count >= _agentBackpressure.EffectiveHighWatermark ? WatermarkLevel.High
                   : WatermarkLevel.Normal;
        if (level != WatermarkLevel.Normal) {
            TryPublish(new WatermarkReachedEvt<TMessage>(agentId, level, count, _agentBackpressure.Capacity));
        }
    }

    /// <summary>
    /// 释放邮箱 — 完成所有 Agent Channel 后释放基类。
    /// </summary>
    public override ValueTask DisposeAsync() {
        foreach (var entry in _agentMailboxes.Values) {
            entry.Channel.Writer.TryComplete();
        }
        _agentMailboxes.Clear();
        return base.DisposeAsync();
    }
}