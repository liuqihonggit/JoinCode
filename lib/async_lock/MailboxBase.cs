namespace Core.Utils;

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
public abstract class MailboxBase<TMessage> : ActorBase<MailboxCmd<TMessage>, MailboxEvt<TMessage>>
{
    private readonly ConcurrentDictionary<string, Channel<TMessage>> _agentChannels;
    private readonly ConcurrentDictionary<string, string> _agentSessions;
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
        : base(commandBackpressure, outputCapacity)
    {
        _agentChannels = new ConcurrentDictionary<string, Channel<TMessage>>();
        _agentSessions = new ConcurrentDictionary<string, string>();
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
    public IAsyncEnumerable<TMessage> ReceiveAsync(string agentId, CancellationToken ct = default)
    {
        if (_agentChannels.TryGetValue(agentId, out var channel))
        {
            return channel.Reader.ReadAllAsync(ct);
        }
        return AsyncEnumerable.Empty<TMessage>();
    }

    /// <summary>获取所有已注册 Agent 标识 — 零拷贝键视图。</summary>
    public IEnumerable<string> GetRegisteredAgents() => _agentChannels.Keys;

    /// <summary>获取指定 Agent 关联的会话 ID。</summary>
    /// <param name="agentId">Agent 标识</param>
    /// <returns>会话 ID；未关联时返回 null</returns>
    public string? GetSessionId(string agentId) => _agentSessions.GetValueOrDefault(agentId);

    /// <summary>获取指定 Agent 通道的当前消息数（无界通道返回 0）。</summary>
    public int GetAgentMessageCount(string agentId)
        => _agentChannels.TryGetValue(agentId, out var ch) && ch.Reader.CanCount ? ch.Reader.Count : 0;

    /// <summary>指定 Agent 是否达到高水位线（生产方应限速）。</summary>
    public bool IsAgentHighWatermark(string agentId)
        => _agentChannels.TryGetValue(agentId, out var ch)
           && ch.Reader.CanCount
           && ch.Reader.Count >= _agentBackpressure.EffectiveHighWatermark;

    /// <summary>指定 Agent 是否达到危险水位线（即将满）。</summary>
    public bool IsAgentCriticalWatermark(string agentId)
        => _agentChannels.TryGetValue(agentId, out var ch)
           && ch.Reader.CanCount
           && ch.Reader.Count >= _agentBackpressure.EffectiveCriticalWatermark;

    /// <summary>Agent 通道背压配置 — 子类和外部可读取用于监控。</summary>
    public ActorBackpressure AgentBackpressure => _agentBackpressure;

    /// <summary>
    /// 命令处理 — Consumer 线程独占执行，路由表无需锁。
    /// </summary>
    /// <param name="cmd">邮箱命令</param>
    /// <param name="ct">取消令牌</param>
    protected override async ValueTask HandleAsync(MailboxCmd<TMessage> cmd, CancellationToken ct)
    {
        switch (cmd)
        {
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
    protected virtual ValueTask HandleSendAsync(string agentId, TMessage message, CancellationToken ct)
    {
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
    protected virtual ValueTask HandleBroadcastAsync(TMessage message, string? excludeAgentId, CancellationToken ct)
    {
        foreach (var kvp in _agentChannels)
        {
            if (kvp.Key != excludeAgentId)
            {
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
    protected void DeliverToAgent(string agentId, TMessage message)
    {
        if (_agentChannels.TryGetValue(agentId, out var channel))
        {
            if (channel.Writer.TryWrite(message))
            {
                CheckAgentWatermark(agentId, channel);
            }
        }
    }

    /// <summary>获取或创建 Agent Channel — 子类可用于直接访问通道。</summary>
    protected Channel<TMessage>? GetAgentChannel(string agentId)
        => _agentChannels.GetValueOrDefault(agentId);

    private void HandleRegisterAgent(string agentId, string? sessionId)
    {
        if (_agentChannels.ContainsKey(agentId)) return;

        var channel = Channel.CreateBounded<TMessage>(new BoundedChannelOptions(_agentBackpressure.Capacity)
        {
            FullMode = _agentBackpressure.FullMode,
            SingleReader = true,
            SingleWriter = false
        });

        _agentChannels[agentId] = channel;

        if (sessionId is not null)
        {
            _agentSessions[agentId] = sessionId;
        }

        TryPublish(new AgentRegisteredEvt<TMessage>(agentId));
    }

    private void HandleUnregisterAgent(string agentId)
    {
        if (_agentChannels.TryRemove(agentId, out var channel))
        {
            channel.Writer.TryComplete();
            TryPublish(new AgentUnregisteredEvt<TMessage>(agentId));
        }
        _agentSessions.TryRemove(agentId, out _);
    }

    private void CheckAgentWatermark(string agentId, Channel<TMessage> channel)
    {
        if (!channel.Reader.CanCount) return;
        var count = channel.Reader.Count;
        var level = count >= _agentBackpressure.EffectiveCriticalWatermark ? WatermarkLevel.Critical
                   : count >= _agentBackpressure.EffectiveHighWatermark ? WatermarkLevel.High
                   : WatermarkLevel.Normal;
        if (level != WatermarkLevel.Normal)
        {
            TryPublish(new WatermarkReachedEvt<TMessage>(agentId, level, count, _agentBackpressure.Capacity));
        }
    }

    /// <summary>
    /// 释放邮箱 — 完成所有 Agent Channel 后释放基类。
    /// </summary>
    public override ValueTask DisposeAsync()
    {
        foreach (var channel in _agentChannels.Values)
        {
            channel.Writer.TryComplete();
        }
        _agentChannels.Clear();
        _agentSessions.Clear();
        return base.DisposeAsync();
    }
}
