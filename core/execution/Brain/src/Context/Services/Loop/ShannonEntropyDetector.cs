namespace Core.Context;

/// <summary>
/// Shannon 信息熵减检测器状态机的状态标志 — [Flags] 位标志枚举
/// <para>
/// 对齐 ADR 0038: 状态机 + 守卫 + [Flags] 位标志降低状态爆炸。
/// 当前状态互斥(Monitoring/Suspected/Confirmed),未来可组合(如 Monitoring|Retrying)。
/// </para>
/// </summary>
[Flags]
public enum EntropyDetectionState : byte
{
    /// <summary>无状态 — 初始或重置后</summary>
    None = 0,

    /// <summary>监控中 — 未检测到熵减趋势或已复位</summary>
    Monitoring = 1,

    /// <summary>疑似死循环 — 第一次触发，等待二次确认</summary>
    Suspected = 2,

    /// <summary>确认死循环 — 确认窗口内二次触发，触发干预</summary>
    Confirmed = 4,
}

/// <summary>
/// 熵减检测器事件枚举 — 驱动状态机转换（ADR 0040）
/// </summary>
public enum EntropyEvent : byte
{
    /// <summary>检测到熵减 — Monitoring→Suspected 或 Confirmed 自循环</summary>
    Decline,

    /// <summary>确认窗口超时 — Suspected→Monitoring</summary>
    Timeout,

    /// <summary>窗口内二次确认 — Suspected→Confirmed</summary>
    Confirm,

    /// <summary>熵恢复 — Confirmed→Monitoring</summary>
    Recover,
}

/// <summary>
/// 熵减检测器共享上下文 — ADR 0040 FsmContext 强类型子类
/// </summary>
internal sealed class EntropyFsmContext : FsmContext
{
    public DateTimeOffset? FirstTriggerTime;
    public int TriggerCount;
    public bool IsDeclining;
    public DateTimeOffset Now;
    public TimeSpan Window;
}

/// <summary>
/// Shannon 信息熵减检测器 — 时间窗口二次确认状态机（ADR 0040 企业级状态机）
/// 原理：LLM 进入死循环时，输出越来越重复，字符分布趋于集中，熵值持续下降
/// 状态转换链：Monitoring →(decline>=threshold)→ Suspected →(窗口内再次触发)→ Confirmed
/// 误报消除：Suspected 状态超过确认窗口未再次触发 → 复位到 Monitoring
/// <para>行为流程：获取当前状态 → 查表 → 守卫判定 → 执行动作 → 转移（ADR 0040）</para>
/// </summary>
[FsmStateMachine(typeof(EntropyDetectionState), typeof(EntropyEvent), EntropyDetectionState.Monitoring)]
[Transition(EntropyDetectionState.Monitoring, EntropyEvent.Decline, EntropyDetectionState.Suspected)]
[Transition(EntropyDetectionState.Suspected, EntropyEvent.Confirm, EntropyDetectionState.Confirmed)]
[Transition(EntropyDetectionState.Suspected, EntropyEvent.Timeout, EntropyDetectionState.Monitoring)]
[Transition(EntropyDetectionState.Confirmed, EntropyEvent.Decline, EntropyDetectionState.Confirmed)]
[Transition(EntropyDetectionState.Confirmed, EntropyEvent.Recover, EntropyDetectionState.Monitoring)]
public sealed partial class ShannonEntropyDetector
{
    private readonly int _windowSize;
    private readonly int _declineThreshold;
    private readonly double _minEntropyDelta;
    private readonly TimeSpan _confirmationWindow;
    private readonly Func<DateTimeOffset> _clock;
    private readonly RingBuffer<double> _entropyHistory;
    private readonly Fsm<EntropyDetectionState, EntropyEvent> _fsm;
    private readonly EntropyFsmContext _ctx;

    /// <summary>
    /// 初始化 Shannon 熵减检测器状态机
    /// </summary>
    /// <param name="windowSize">熵值历史窗口大小</param>
    /// <param name="declineThreshold">连续下降轮数阈值（连续 declineThreshold 轮熵递减则进入 Suspected 状态）</param>
    /// <param name="minEntropyDelta">最小熵差阈值（相邻轮熵差需超过此值才算"下降"）</param>
    /// <param name="confirmationWindow">二次确认时间窗口 — Suspected 状态下在此窗口内再次触发则确认死循环</param>
    /// <param name="clock">时钟注入点（仅测试用，生产环境用 DateTimeOffset.UtcNow）</param>
    public ShannonEntropyDetector(
        int windowSize,
        int declineThreshold,
        double minEntropyDelta,
        TimeSpan confirmationWindow,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(declineThreshold, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(minEntropyDelta);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(confirmationWindow, TimeSpan.Zero);

        _windowSize = windowSize;
        _declineThreshold = declineThreshold;
        _minEntropyDelta = minEntropyDelta;
        _confirmationWindow = confirmationWindow;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _entropyHistory = new RingBuffer<double>(RingBuffer<double>.RoundUpToPowerOfTwo(windowSize * 2));
        _ctx = new EntropyFsmContext();
        _fsm = new Fsm<EntropyDetectionState, EntropyEvent>(_fsmTable, EntropyDetectionState.Monitoring);
        _fsm.StateChanged += (_, e) => FsmDispatchEvent(e);
    }

    /// <summary>
    /// 记录一轮文本，计算 Shannon 熵并驱动状态机转换
    /// </summary>
    public ShannonEntropyResult Record(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length < 10)
            return new ShannonEntropyResult(_fsm.CurrentState, false, 0, 0, _ctx.TriggerCount);

        var entropy = ComputeShannonEntropy(text);
        _entropyHistory.Add(entropy);

        var declineStreak = CountConsecutiveDecline();
        _ctx.IsDeclining = declineStreak >= _declineThreshold;
        _ctx.Now = _clock();
        _ctx.Window = _confirmationWindow;

        var evt = SelectEvent(_fsm.CurrentState, _ctx);
        if (evt.HasValue)
            _fsm.Trigger(evt.Value, _ctx);

        if (evt == EntropyEvent.Timeout && _ctx.IsDeclining)
            _fsm.Trigger(EntropyEvent.Decline, _ctx);

        var isLoop = _fsm.CurrentState == EntropyDetectionState.Confirmed;
        return new ShannonEntropyResult(_fsm.CurrentState, isLoop, entropy, declineStreak, _ctx.TriggerCount);
    }

    /// <summary>重置检测器状态机和所有历史</summary>
    public void Reset()
    {
        _entropyHistory.Clear();
        _ctx.TriggerCount = 0;
        _ctx.FirstTriggerTime = null;
        _fsm.Reset(EntropyDetectionState.Monitoring);
    }

    /// <summary>当前状态机状态</summary>
    public EntropyDetectionState State => _fsm.CurrentState;

    public int TriggerCount => _ctx.TriggerCount;

    private static EntropyEvent? SelectEvent(EntropyDetectionState state, EntropyFsmContext ctx)
    {
        return state switch
        {
            EntropyDetectionState.Monitoring => ctx.IsDeclining ? EntropyEvent.Decline : null,
            EntropyDetectionState.Suspected => SelectSuspectedEvent(ctx),
            EntropyDetectionState.Confirmed => ctx.IsDeclining ? EntropyEvent.Decline : EntropyEvent.Recover,
            _ => null,
        };
    }

    private static EntropyEvent? SelectSuspectedEvent(EntropyFsmContext ctx)
    {
        var inWindow = (ctx.Now - (ctx.FirstTriggerTime ?? ctx.Now)) <= ctx.Window;
        if (!inWindow)
            return EntropyEvent.Timeout;
        return ctx.IsDeclining ? EntropyEvent.Confirm : null;
    }

    [TransitionAction(EntropyDetectionState.Monitoring, EntropyEvent.Decline)]
    private static void FsmActDeclineFromMonitoring(FsmContext? ctx)
    {
        var c = (EntropyFsmContext)ctx!;
        c.FirstTriggerTime = c.Now;
    }

    [TransitionAction(EntropyDetectionState.Suspected, EntropyEvent.Confirm)]
    private static void FsmActConfirm(FsmContext? ctx) => ((EntropyFsmContext)ctx!).TriggerCount++;

    [TransitionAction(EntropyDetectionState.Suspected, EntropyEvent.Timeout)]
    private static void FsmActTimeout(FsmContext? ctx) => ((EntropyFsmContext)ctx!).FirstTriggerTime = null;

    [TransitionAction(EntropyDetectionState.Confirmed, EntropyEvent.Decline)]
    private static void FsmActDeclineFromConfirmed(FsmContext? ctx) => ((EntropyFsmContext)ctx!).TriggerCount++;

    [TransitionAction(EntropyDetectionState.Confirmed, EntropyEvent.Recover)]
    private static void FsmActRecover(FsmContext? ctx) => ((EntropyFsmContext)ctx!).FirstTriggerTime = null;

    /// <summary>
    /// 计算 Shannon 信息熵 H = -Σ(p_i * log2(p_i))
    /// </summary>
    private static double ComputeShannonEntropy(string text)
    {
        if (text.Length == 0)
            return 0.0;

        var freq = new Dictionary<char, int>();
        foreach (var c in text)
        {
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(freq, c, out _);
            count++;
        }

        var entropy = 0.0;
        var len = (double)text.Length;

        foreach (var kvp in freq)
        {
            var p = kvp.Value / len;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    /// <summary>
    /// 计算连续下降轮数（从最新往回看，每轮熵差超过 minEntropyDelta 才算下降）
    /// </summary>
    private int CountConsecutiveDecline()
    {
        if (_entropyHistory.Count < 2)
            return 0;

        var streak = 0;
        for (var i = _entropyHistory.Count - 1; i >= 1; i--)
        {
            var delta = _entropyHistory[i - 1] - _entropyHistory[i];
            if (delta >= _minEntropyDelta)
            {
                streak++;
            }
            else
            {
                break;
            }
        }

        return streak;
    }
}

/// <summary>
/// Shannon 熵减检测结果 — 携带状态机当前状态
/// </summary>
public sealed record ShannonEntropyResult(
    EntropyDetectionState State,
    bool IsLoopDetected,
    double CurrentEntropy,
    int DeclineStreak,
    int TriggerCount)
{
    /// <summary>
    /// 只有 Confirmed 状态才 IsLoopDetected=true，触发 LoopDetected 事件
    /// </summary>
    public static readonly ShannonEntropyResult NoLoop = new(EntropyDetectionState.Monitoring, false, 0, 0, 0);
}
