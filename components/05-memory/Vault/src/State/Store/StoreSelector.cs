
namespace State;

/// <summary>
/// 派生状态选择器实现
/// 仅当派生值变化时通知订阅者
/// 使用不可变集合 + 无锁原子操作确保线程安全
/// </summary>
public sealed class StoreSelector<TState, TSelected> : IStoreSelector<TState, TSelected>, IDisposable
    where TState : notnull
{
    private readonly IStore<TState> _store;
    private readonly Func<TState, TSelected> _selector;
    private readonly IEqualityComparer<TSelected> _comparer;
    private ImmutableList<Action<TSelected>> _subscribers = ImmutableList<Action<TSelected>>.Empty;

    private TSelected _currentValue;
    private IDisposable? _storeSubscription;
    private int _disposed;

    /// <summary>
    /// 创建选择器
    /// </summary>
    public StoreSelector(
        IStore<TState> store,
        Func<TState, TSelected> selector,
        IEqualityComparer<TSelected>? comparer = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _comparer = comparer ?? EqualityComparer<TSelected>.Default;

        _currentValue = _selector(store.GetState());

        _storeSubscription = store.Subscribe(OnStoreStateChanged);
    }

    /// <inheritdoc />
    public Func<TState, TSelected> Selector => _selector;

    /// <inheritdoc />
    public TSelected CurrentValue => Interlocked.CompareExchange(ref _currentValue, default!, default!);

    /// <inheritdoc />
    public IDisposable Subscribe(Action<TSelected> handler)
    {
        ThrowIfDisposed();

        ImmutableInterlocked.Update(ref _subscribers, s => s.Add(handler));
        handler(Interlocked.CompareExchange(ref _currentValue, default!, default!));

        return new SelectorSubscriptionDisposable(this, handler);
    }

    /// <summary>
    /// 处理 Store 状态变更
    /// </summary>
    private void OnStoreStateChanged(StateChangedEventArgs<TState> args)
    {
        var newValue = _selector(args.NewState);
        var oldValue = Interlocked.CompareExchange(ref _currentValue, default!, default!);

        if (_comparer.Equals(oldValue, newValue))
        {
            return;
        }

        Interlocked.Exchange(ref _currentValue, newValue);

        var snapshot = Volatile.Read(ref _subscribers);
        foreach (var subscriber in snapshot)
        {
            try
            {
                subscriber(newValue);
            }
            catch (Exception ex)
            {
                // 订阅者异常不应中断其他订阅者的通知，但需记录日志
                System.Diagnostics.Trace.WriteLine($"StoreSelector subscriber threw: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    internal void Unsubscribe(Action<TSelected> handler)
    {
        ImmutableInterlocked.Update(ref _subscribers, s => s.Remove(handler));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, typeof(StoreSelector<TState, TSelected>));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        _storeSubscription?.Dispose();
        ImmutableInterlocked.Update(ref _subscribers, _ => ImmutableList<Action<TSelected>>.Empty);
    }

    /// <summary>
    /// 选择器订阅可释放对象
    /// </summary>
    private sealed class SelectorSubscriptionDisposable : IDisposable
    {
        private readonly StoreSelector<TState, TSelected> _selector;
        private readonly Action<TSelected> _handler;
        private int _disposed;

        public SelectorSubscriptionDisposable(
            StoreSelector<TState, TSelected> selector,
            Action<TSelected> handler)
        {
            _selector = selector;
            _handler = handler;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _selector.Unsubscribe(_handler);
        }
    }
}

/// <summary>
/// 选择器组合工具
/// </summary>
public static class SelectorComposition
{
    /// <summary>
    /// 组合两个选择器
    /// </summary>
    public static Func<TState, (T1, T2)> Combine<TState, T1, T2>(
        Func<TState, T1> selector1,
        Func<TState, T2> selector2)
        where TState : notnull
    {
        return state => (selector1(state), selector2(state));
    }

    /// <summary>
    /// 组合三个选择器
    /// </summary>
    public static Func<TState, (T1, T2, T3)> Combine<TState, T1, T2, T3>(
        Func<TState, T1> selector1,
        Func<TState, T2> selector2,
        Func<TState, T3> selector3)
        where TState : notnull
    {
        return state => (selector1(state), selector2(state), selector3(state));
    }

    /// <summary>
    /// 创建记忆化选择器（缓存结果避免重复计算）
    /// </summary>
    public static Func<TState, TSelected> Memoized<TState, TSelected>(
        Func<TState, TSelected> selector,
        IEqualityComparer<TState>? stateComparer = null)
        where TState : notnull
    {
        var comparer = stateComparer ?? EqualityComparer<TState>.Default;
        TState? lastState = default;
        TSelected? lastResult = default;
        var initialized = false;

        return state =>
        {
            if (initialized && comparer.Equals(lastState!, state))
            {
                return lastResult!;
            }

            lastState = state;
            lastResult = selector(state);
            initialized = true;
            return lastResult;
        };
    }
}

/// <summary>
/// 引用相等比较器
/// </summary>
public sealed class ReferenceEqualityComparer : IEqualityComparer<object>
{
    public static ReferenceEqualityComparer Instance { get; } = new();

    private ReferenceEqualityComparer() { }

    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

    public int GetHashCode(object? obj) => RuntimeHelpers.GetHashCode(obj);
}
