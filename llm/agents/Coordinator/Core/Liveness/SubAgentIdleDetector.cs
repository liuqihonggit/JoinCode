namespace Core.Agents.Coordinator.Liveness;

/// <summary>
/// 子代理活性检测状态 — [Flags] 位标志枚举，对齐 ADR 0038 状态机+守卫模式
/// </summary>
[Flags]
public enum SubAgentLivenessState : byte
{
    /// <summary>无状态 — 初始或重置后</summary>
    None = 0,

    /// <summary>监控中 — 子代理正常活动</summary>
    Monitoring = 1,

    /// <summary>疑似卡死 — 检测到无活动，等待二次确认</summary>
    Suspected = 2,

    /// <summary>确认卡死 — 二次确认窗口内仍无活动，触发干预</summary>
    Confirmed = 4,
}

/// <summary>
/// 活性检测事件 — 驱动状态机转换
/// </summary>
public enum SubAgentLivenessEvent : byte
{
    /// <summary>检测到无活动 — Monitoring→Suspected 或 Confirmed 自循环</summary>
    Idle,

    /// <summary>检测到有活动 — Suspected→Monitoring（恢复）</summary>
    Active,

    /// <summary>二次确认窗口内再次 Idle — Suspected→Confirmed</summary>
    Confirm,

    /// <summary>确认窗口超时 — Suspected→Monitoring（误报消除）</summary>
    Timeout,

    /// <summary>干预后恢复活动 — Confirmed→Monitoring</summary>
    Recover,

    /// <summary>手动重置 — *→Monitoring</summary>
    Reset,
}

/// <summary>
/// 子代理无输出检测结果 — 携带状态机当前状态和事件
/// </summary>
/// <param name="State">状态机当前状态</param>
/// <param name="Event">触发的事件（null=无事件）</param>
/// <param name="IsStalled">是否确认卡死（仅 Confirmed 状态为 true）</param>
public sealed record SubAgentIdleResult(
    SubAgentLivenessState State,
    SubAgentLivenessEvent? Event,
    bool IsStalled)
{
    /// <summary>未检测到卡死的默认结果</summary>
    public static readonly SubAgentIdleResult NotStalled = new(SubAgentLivenessState.Monitoring, null, false);
}

/// <summary>
/// 子代理无输出检测器 — 状态机 + 时间窗口二次确认（ADR 0106 L2 检测层）
/// <para>
/// 检测条件：LastActivityAt 超过阈值 且 无孙代理（有孙代理说明在等待子任务，不算卡死）。
/// 状态转换链：Monitoring →(Idle)→ Suspected →(窗口内再次Idle)→ Confirmed
/// 误报消除：Suspected 状态超过确认窗口未再次触发 → 复位到 Monitoring
/// </para>
/// <para>复用 ShannonEntropyDetector 的状态机模式（ADR 0040），但检测信号从"熵减"改为"无活动时间超限"</para>
/// </summary>
public sealed class SubAgentIdleDetector
{
    private SubAgentLivenessState _state = SubAgentLivenessState.Monitoring;
    private DateTimeOffset _suspectedAt;
    private readonly TimeSpan _idleThreshold;
    private readonly TimeSpan _confirmationWindow;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>
    /// 初始化子代理无输出检测器
    /// </summary>
    /// <param name="idleThreshold">无活动超时阈值（默认 30s）</param>
    /// <param name="confirmationWindow">二次确认时间窗口（默认 5s）</param>
    /// <param name="clock">时钟注入点（仅测试用，生产用 DateTimeOffset.UtcNow）</param>
    public SubAgentIdleDetector(
        TimeSpan? idleThreshold = null,
        TimeSpan? confirmationWindow = null,
        Func<DateTimeOffset>? clock = null)
    {
        _idleThreshold = idleThreshold ?? TimeSpan.FromSeconds(30);
        _confirmationWindow = confirmationWindow ?? TimeSpan.FromSeconds(5);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>当前状态机状态</summary>
    public SubAgentLivenessState State => _state;

    /// <summary>是否确认卡死</summary>
    public bool IsConfirmed => _state == SubAgentLivenessState.Confirmed;

    /// <summary>是否疑似卡死（等待二次确认）</summary>
    public bool IsSuspected => _state == SubAgentLivenessState.Suspected;

    /// <summary>
    /// 记录一次活动检测，驱动状态机转换
    /// </summary>
    /// <param name="lastActivityAt">子代理最后活跃时刻</param>
    /// <param name="hasGrandchildren">是否有活跃的孙代理</param>
    /// <returns>检测结果（含新状态和触发事件）</returns>
    public SubAgentIdleResult Record(DateTimeOffset lastActivityAt, bool hasGrandchildren)
    {
        var now = _clock();
        var idleSpan = now - lastActivityAt;
        var isIdle = idleSpan > _idleThreshold && !hasGrandchildren;

        var evt = SelectEvent(isIdle, now);
        if (evt.HasValue)
        {
            _state = Transition(_state, evt.Value);
            if (evt.Value == SubAgentLivenessEvent.Idle && _state == SubAgentLivenessState.Suspected)
                _suspectedAt = now;
        }

        var isStalled = _state == SubAgentLivenessState.Confirmed;
        return new SubAgentIdleResult(_state, evt, isStalled);
    }

    /// <summary>
    /// 标记恢复 — 干预后子代理恢复活动时调用
    /// </summary>
    public void MarkRecovered()
    {
        if (_state == SubAgentLivenessState.Confirmed)
            _state = SubAgentLivenessState.Monitoring;
    }

    /// <summary>重置检测器到 Monitoring 状态</summary>
    public void Reset()
    {
        _state = SubAgentLivenessState.Monitoring;
        _suspectedAt = default;
    }

    private SubAgentLivenessEvent? SelectEvent(bool isIdle, DateTimeOffset now)
    {
        return _state switch
        {
            SubAgentLivenessState.Monitoring => isIdle ? SubAgentLivenessEvent.Idle : null,
            SubAgentLivenessState.Suspected => SelectSuspectedEvent(isIdle, now),
            SubAgentLivenessState.Confirmed => isIdle ? null : SubAgentLivenessEvent.Recover,
            _ => null,
        };
    }

    private SubAgentLivenessEvent? SelectSuspectedEvent(bool isIdle, DateTimeOffset now)
    {
        var inWindow = (now - _suspectedAt) <= _confirmationWindow;
        if (!inWindow)
            return isIdle ? SubAgentLivenessEvent.Confirm : SubAgentLivenessEvent.Timeout;
        return isIdle ? SubAgentLivenessEvent.Confirm : SubAgentLivenessEvent.Active;
    }

    private static SubAgentLivenessState Transition(SubAgentLivenessState state, SubAgentLivenessEvent evt)
    {
        return (state, evt) switch
        {
            (SubAgentLivenessState.Monitoring, SubAgentLivenessEvent.Idle) => SubAgentLivenessState.Suspected,
            (SubAgentLivenessState.Suspected, SubAgentLivenessEvent.Confirm) => SubAgentLivenessState.Confirmed,
            (SubAgentLivenessState.Suspected, SubAgentLivenessEvent.Timeout) => SubAgentLivenessState.Monitoring,
            (SubAgentLivenessState.Suspected, SubAgentLivenessEvent.Active) => SubAgentLivenessState.Monitoring,
            (SubAgentLivenessState.Confirmed, SubAgentLivenessEvent.Recover) => SubAgentLivenessState.Monitoring,
            (SubAgentLivenessState.Confirmed, SubAgentLivenessEvent.Idle) => SubAgentLivenessState.Confirmed,
            (_, SubAgentLivenessEvent.Reset) => SubAgentLivenessState.Monitoring,
            _ => state,
        };
    }
}
