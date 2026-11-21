
namespace State;

/// <summary>
/// 派生状态选择器实现
/// 仅当派生值变化时通知订阅者
/// 使用不可变集合 + 轻量级锁确保线程安全，同时兼容值类型（含 tuple）选择结果
/// </summary>
public sealed class StoreSelector<TState, TSelected> : IStoreSelector<TState, TSelected>, IDisposable
    where TState : notnull {
    private readonly IStore<TState> _store;
    private readonly Func<TState, TSelected> _selector;
    private readonly IEqualityComparer<TSelected> _comparer;
    private ImmutableList<Action<TSelected>> _subscribers = ImmutableList<Action<TSelected>>.Empty;
    private readonly ILogger<StoreSelector<TState, TSelected>>? _logger;

    private TSelected _currentValue;
    private readonly IDisposable? _storeSubscription;
    private int _disposed;

    /// <summary>
    /// 创建选择器
    /// </summary>
    public StoreSelector(
        IStore<TState> store,
        Func<TState, TSelected> selector,
        IEqualityComparer<TSelected>? comparer = null,
        ILogger<StoreSelector<TState, TSelected>>? logger = null) {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _comparer = comparer ?? EqualityComparer<TSelected>.Default;
        _logger = logger;

        _currentValue = _selector(store.GetState());

        _storeSubscription = store.Subscribe(OnStoreStateChanged);
    }

    /// <inheritdoc />
    public Func<TState, TSelected> Selector => _selector;

    /// <inheritdoc />
    public TSelected CurrentValue {
        get {
            Interlocked.MemoryBarrier();
            return _currentValue;
        }
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Action<TSelected> handler) {
        ThrowIfDisposed();

        ImmutableInterlocked.Update(ref _subscribers, s => s.Add(handler));

        try {
            handler(CurrentValue);
        } catch (Exception ex) {
            // 订阅者异常不应中断其他订阅者的通知，但需记录日志
            _logger?.LogWarning(ex, "StoreSelector 订阅者抛出异常");
        }

        return new SelectorSubscriptionDisposable(this, handler);
    }

    /// <summary>
    /// 处理 Store 状态变更
    /// </summary>
    private void OnStoreStateChanged(StateChangedEventArgs<TState> args) {
        var newValue = _selector(args.NewState);
        Interlocked.MemoryBarrier();
        var oldValue = _currentValue;

        if (_comparer.Equals(oldValue, newValue)) {
            return;
        }

        _currentValue = newValue;
        Interlocked.MemoryBarrier();

        var snapshot = Volatile.Read(ref _subscribers);
        foreach (var subscriber in snapshot) {
            try {
                subscriber(newValue);
            } catch (Exception ex) {
                // 订阅者异常不应中断其他订阅者的通知，但需记录日志
                _logger?.LogWarning(ex, "StoreSelector 订阅者抛出异常");
            }
        }
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    internal void Unsubscribe(Action<TSelected> handler) {
        ImmutableInterlocked.Update(ref _subscribers, s => s.Remove(handler));
    }

    private void ThrowIfDisposed() {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, typeof(StoreSelector<TState, TSelected>));
    }

    /// <summary>
    /// 释放选择器 — 取消 Store 订阅并清空所有订阅者,确保幂等。
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _storeSubscription?.Dispose();
        ImmutableInterlocked.Update(ref _subscribers, _ => ImmutableList<Action<TSelected>>.Empty);
    }

    /// <summary>
    /// 选择器订阅可释放对象
    /// </summary>
    private sealed class SelectorSubscriptionDisposable : IDisposable {
        private readonly StoreSelector<TState, TSelected> _selector;
        private readonly Action<TSelected> _handler;
        private int _disposed;

        /// <summary>
        /// 构造函数 — 绑定选择器与订阅处理器。
        /// </summary>
        public SelectorSubscriptionDisposable(
            StoreSelector<TState, TSelected> selector,
            Action<TSelected> handler) {
            _selector = selector;
            _handler = handler;
        }

        /// <summary>
        /// 释放时取消订阅。
        /// </summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _selector.Unsubscribe(_handler);
        }
    }
}

/// <summary>
/// 引用相等比较器
/// </summary>
public sealed class ReferenceEqualityComparer : IEqualityComparer<object> {
    /// <summary>
    /// 单例实例。
    /// </summary>
    public static ReferenceEqualityComparer Instance { get; } = new();

    private ReferenceEqualityComparer() { }

    /// <summary>
    /// 使用引用相等比较两个对象。
    /// </summary>
    /// <param name="x">第一个对象。</param>
    /// <param name="y">第二个对象。</param>
    /// <returns>若两者引用相同则返回 true。</returns>
    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

    /// <summary>
    /// 基于 RuntimeHelpers 获取对象哈希码,确保与引用相等语义一致。
    /// </summary>
    /// <param name="obj">待计算哈希的对象。</param>
    /// <returns>对象哈希码。</returns>
    public int GetHashCode(object? obj) => RuntimeHelpers.GetHashCode(obj);
}