
namespace State;

/// <summary>
/// 响应式状态存储实现
/// 使用不可变数据结构 + 无锁原子操作确保线程安全
/// </summary>
public partial class Store<TState> : IStore<TState>, IDisposable where TState : notnull
{
    private TState _currentState;
    private ImmutableHashSet<StateChangedHandler<TState>> _subscribers = ImmutableHashSet<StateChangedHandler<TState>>.Empty;
    private readonly IStorePersistence<TState>? _persistence;
    [Inject] private readonly ILogger<Store<TState>>? _logger;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

    /// <summary>
    /// 创建 Store 实例
    /// </summary>
    /// <param name="initialState">初始状态</param>
    /// <param name="persistence">可选的持久化插件</param>
    /// <param name="logger">可选的日志记录器</param>
    public Store(
        TState initialState,
        IStorePersistence<TState>? persistence = null,
        ILogger<Store<TState>>? logger = null)
    {
        _currentState = initialState;
        _persistence = persistence;
        _logger = logger;
    }

    /// <summary>
    /// 从持久化加载状态创建 Store
    /// </summary>
    public static async Task<Store<TState>> CreateAsync(
        TState defaultState,
        IStorePersistence<TState>? persistence = null,
        ILogger<Store<TState>>? logger = null,
        CancellationToken cancellationToken = default)
    {
        TState initialState = defaultState;

        if (persistence != null)
        {
            try
            {
                var loadedState = await persistence.LoadAsync(cancellationToken).ConfigureAwait(false);
                if (loadedState != null)
                {
                    initialState = loadedState;
                    logger?.LogInformation(L.T(StringKey.VaultLogStateLoadedFromPersistStore));
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, L.T(StringKey.VaultLogStateLoadFailedDefault));
            }
        }

        return new Store<TState>(initialState, persistence, logger);
    }

    /// <inheritdoc />
    public TState GetState()
    {
        return Interlocked.CompareExchange(ref _currentState, default!, default!);
    }

    /// <inheritdoc />
    public Task<TState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetState());
    }

    /// <inheritdoc />
    public void SetState(Func<TState, TState> updater)
    {
        ThrowIfDisposed();

        while (true)
        {
            var oldState = Interlocked.CompareExchange(ref _currentState, default!, default!);
            var newState = updater(oldState);

            if (ReferenceEquals(oldState, newState))
            {
                _logger?.LogDebug(L.T(StringKey.VaultLogStateUnchanged));
                return;
            }

            if (Interlocked.CompareExchange(ref _currentState, newState, oldState).Equals(oldState))
            {
                NotifySubscribers(oldState, newState);

                if (_persistence != null)
                {
                    _ = PersistStateAsync(newState, _disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
                }

                return;
            }
        }
    }

    /// <inheritdoc />
    public async Task SetStateAsync(Func<TState, Task<TState>> updater, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        while (true)
        {
            var oldState = Interlocked.CompareExchange(ref _currentState, default!, default!);
            var newState = await updater(oldState).ConfigureAwait(false);

            if (ReferenceEquals(oldState, newState))
            {
                _logger?.LogDebug(L.T(StringKey.VaultLogAsyncStateUnchanged));
                return;
            }

            if (Interlocked.CompareExchange(ref _currentState, newState, oldState).Equals(oldState))
            {
                NotifySubscribers(oldState, newState);

                if (_persistence != null)
                {
                    _ = PersistStateAsync(newState, _disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
                }

                return;
            }
        }
    }

    /// <inheritdoc />
    public IDisposable Subscribe(StateChangedHandler<TState> handler)
    {
        ThrowIfDisposed();

        ImmutableInterlocked.Update(ref _subscribers, s => s.Add(handler));

        return new SubscriptionDisposable(this, handler);
    }

    /// <inheritdoc />
    public IDisposable Subscribe(IStateSubscriber<TState> subscriber)
    {
        return Subscribe(subscriber.OnStateChanged);
    }

    /// <summary>
    /// 通知所有订阅者状态变更
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NotifySubscribers(TState oldState, TState newState)
    {
        var args = new StateChangedEventArgs<TState>(oldState, newState);
        var currentSubscribers = Volatile.Read(ref _subscribers);

        foreach (var subscriber in currentSubscribers)
        {
            try
            {
                subscriber(args);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, L.T(StringKey.VaultLogSubscriberError));
            }
        }
    }

    /// <summary>
    /// 异步持久化状态 — 带超时和取消保护
    /// </summary>
    private async Task PersistStateAsync(TState state, CancellationToken cancellationToken)
    {
        if (_persistence == null) return;

        try
        {
            await _persistence.SaveAsync(state, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
            _logger?.LogDebug(L.T(StringKey.VaultLogStatePersistedStore));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogStatePersistFailedStore));
        }
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    internal void Unsubscribe(StateChangedHandler<TState> handler)
    {
        ImmutableInterlocked.Update(ref _subscribers, s => s.Remove(handler));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, typeof(Store<TState>));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        _disposeCts.Cancel();
        _disposeCts.Dispose();

        ImmutableInterlocked.Update(ref _subscribers, _ => ImmutableHashSet<StateChangedHandler<TState>>.Empty);
    }

    /// <summary>
    /// 订阅可释放对象
    /// </summary>
    private sealed class SubscriptionDisposable : IDisposable
    {
        private readonly Store<TState> _store;
        private readonly StateChangedHandler<TState> _handler;
        private int _disposed;

        public SubscriptionDisposable(Store<TState> store, StateChangedHandler<TState> handler)
        {
            _store = store;
            _handler = handler;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _store.Unsubscribe(_handler);
        }
    }
}
