namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 状态机转换键 — (FromState, Event) 组合键
/// <para>ADR 0040: 禁止用元组，用 readonly record struct 显式命名</para>
/// <para>ADR 0041: 实现 IComparable 支持排序数组 + Array.BinarySearch 查找（替代 FrozenDictionary 哈希桶）</para>
/// </summary>
public readonly record struct TransitionKey<TState, TEvent>(TState From, TEvent Event)
    : IComparable<TransitionKey<TState, TEvent>>
    where TState : struct, Enum
    where TEvent : struct, Enum
{
    public int CompareTo(TransitionKey<TState, TEvent> other)
    {
        var fromCmp = Comparer<TState>.Default.Compare(From, other.From);
        return fromCmp != 0 ? fromCmp : Comparer<TEvent>.Default.Compare(Event, other.Event);
    }
}

/// <summary>
/// 守卫委托 — 条件检查，返回 true 才允许转换
/// </summary>
public delegate bool TransitionGuard(FsmContext? ctx);

/// <summary>
/// 动作委托 — 转换时执行的副作用（状态转换后执行）
/// </summary>
public delegate void TransitionAction(FsmContext? ctx);

/// <summary>
/// 状态机共享上下文基类 — 所有状态共享的数据，可选（简单场景不用）
/// <para>用户继承此类定义强类型上下文，AOT 友好</para>
/// <para>熔断标记不放在此处，作为状态枚举的 Faulted 状态值</para>
/// </summary>
public class FsmContext;

/// <summary>
/// 状态机转换规则 — 目标状态 + 守卫 + 动作
/// </summary>
public sealed record TransitionRule<TState>(
    TState Target,
    TransitionGuard? Guard = null,
    TransitionAction? Action = null)
    where TState : struct, Enum;

/// <summary>
/// 转换结果类型
/// </summary>
public enum TransitionOutcome
{
    /// <summary>转换成功</summary>
    Transitioned,

    /// <summary>转换表无此 (FromState, Event) 组合</summary>
    NoRule,

    /// <summary>守卫检查失败</summary>
    GuardFailed,
}

/// <summary>
/// 状态机转换结果
/// </summary>
public sealed record TransitionResult<TState, TEvent>(
    bool Transitioned,
    TState FromState,
    TState ToState,
    TEvent Event,
    TransitionOutcome Outcome)
    where TState : struct, Enum
    where TEvent : struct, Enum;

/// <summary>
/// 企业级状态机 — 转换表 + 守卫 + 共享上下文 + 事件枚举
/// <para>行为流程：获取当前状态 → 查表得到事件函数指针 → 守卫判定 → 执行动作 → 转移</para>
/// <para>ADR 0040: 禁止元组 key，用 TransitionKey record struct；熔断作为状态枚举值</para>
/// <para>ADR 0041: 排序并行数组 + Array.BinarySearch 替代 FrozenDictionary（省内存，连续缓存友好）</para>
/// <para>线程安全：状态读写用 lock，Action 在 lock 外执行（避免长持锁）</para>
/// </summary>
public sealed class Fsm<TState, TEvent>
    where TState : struct, Enum
    where TEvent : struct, Enum
{
    private readonly TransitionKey<TState, TEvent>[] _sortedKeys;
    private readonly TransitionRule<TState>[] _rules;
    private readonly AsyncLock _lock = new("Fsm");
    private TState _currentState;

    /// <summary>当前状态（线程安全读取）</summary>
    public TState CurrentState
    {
        get { using (_lock.Lock()) { return _currentState; } }
    }

    /// <summary>状态变更事件（转换成功后触发）</summary>
    public event EventHandler<TransitionResult<TState, TEvent>>? StateChanged;

    /// <summary>
    /// 构造状态机（排序并行数组 — 生成器首选，零构造开销）
    /// </summary>
    /// <param name="sortedKeys">已按 TransitionKey.CompareTo 排序的 key 数组</param>
    /// <param name="rules">与 sortedKeys 并行对应的 rule 数组</param>
    /// <param name="initialState">初始状态</param>
    public Fsm(
        TransitionKey<TState, TEvent>[] sortedKeys,
        TransitionRule<TState>[] rules,
        TState initialState)
    {
        _sortedKeys = sortedKeys;
        _rules = rules;
        _currentState = initialState;
    }

    /// <summary>
    /// 构造状态机（FrozenDictionary 兼容构造 — 内部转排序数组）
    /// </summary>
    public Fsm(
        FrozenDictionary<TransitionKey<TState, TEvent>, TransitionRule<TState>> table,
        TState initialState)
    {
        var pairs = table.OrderBy(kvp => kvp.Key).ToArray();
        _sortedKeys = pairs.Select(p => p.Key).ToArray();
        _rules = pairs.Select(p => p.Value).ToArray();
        _currentState = initialState;
    }

    /// <summary>
    /// 二分查找转换规则 — O(log n)，n 通常 &lt; 15
    /// </summary>
    private TransitionRule<TState>? LookupRule(TransitionKey<TState, TEvent> key)
    {
        var idx = Array.BinarySearch(_sortedKeys, key);
        return idx >= 0 ? _rules[idx] : null;
    }

    /// <summary>
    /// 触发事件 — 查表 → 守卫判定 → 转移 → 执行动作
    /// <para>线程安全：查表+守卫+状态转移在 lock 内，Action 在 lock 外</para>
    /// </summary>
    /// <param name="evt">触发的事件</param>
    /// <param name="ctx">共享上下文（可选）</param>
    /// <returns>转换结果</returns>
    public TransitionResult<TState, TEvent> Trigger(TEvent evt, FsmContext? ctx = null)
    {
        TState oldState;
        TState newState;
        TransitionAction? actionToRun;

        using (_lock.Lock())
        {
            oldState = _currentState;
            var key = new TransitionKey<TState, TEvent>(_currentState, evt);
            var rule = LookupRule(key);

            if (rule is null)
                return new TransitionResult<TState, TEvent>(false, oldState, oldState, evt, TransitionOutcome.NoRule);

            if (rule.Guard is not null && !rule.Guard(ctx))
                return new TransitionResult<TState, TEvent>(false, oldState, oldState, evt, TransitionOutcome.GuardFailed);

            newState = rule.Target;
            actionToRun = rule.Action;
            _currentState = newState;
        }

        actionToRun?.Invoke(ctx);

        var result = new TransitionResult<TState, TEvent>(true, oldState, newState, evt, TransitionOutcome.Transitioned);
        StateChanged?.Invoke(this, result);
        return result;
    }

    /// <summary>
    /// 尝试触发事件，返回是否转换成功
    /// </summary>
    public bool TryTrigger(TEvent evt, FsmContext? ctx = null)
        => Trigger(evt, ctx).Transitioned;

    /// <summary>
    /// 检查事件是否可触发（查表 + 守卫通过）
    /// </summary>
    public bool CanTrigger(TEvent evt, FsmContext? ctx = null)
    {
        using (_lock.Lock())
        {
            var key = new TransitionKey<TState, TEvent>(_currentState, evt);
            var rule = LookupRule(key);
            if (rule is null)
                return false;

            return rule.Guard is null || rule.Guard(ctx);
        }
    }

    /// <summary>
    /// 获取当前状态下所有可触发的事件（守卫通过的事件）
    /// </summary>
    public IReadOnlyList<TEvent> GetAvailableEvents(FsmContext? ctx = null)
    {
        using (_lock.Lock())
        {
            var state = _currentState;
            var result = new List<TEvent>();
            for (var i = 0; i < _sortedKeys.Length; i++)
            {
                if (_sortedKeys[i].From.Equals(state))
                {
                    var rule = _rules[i];
                    if (rule.Guard is null || rule.Guard(ctx))
                        result.Add(_sortedKeys[i].Event);
                }
            }
            return result;
        }
    }

    /// <summary>
    /// 强制设置状态（仅用于测试/恢复场景，跳过守卫和动作）
    /// </summary>
    public void ForceSet(TState state)
    {
        TState oldState;
        using (_lock.Lock())
        {
            oldState = _currentState;
            _currentState = state;
        }

        if (!oldState.Equals(state))
            StateChanged?.Invoke(this, new TransitionResult<TState, TEvent>(true, oldState, state, default, TransitionOutcome.Transitioned));
    }

    /// <summary>
    /// 重置到指定状态（不触发事件）
    /// </summary>
    public void Reset(TState state)
    {
        using (_lock.Lock())
        {
            _currentState = state;
        }
    }
}
