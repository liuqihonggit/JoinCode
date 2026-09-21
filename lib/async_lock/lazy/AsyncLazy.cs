namespace Core.Utils;

public sealed class AsyncLazy<T> : IAsyncLazy<T> {
    private readonly Func<Task<T>> _factory;
    private readonly AsyncLock _gate = new($"{typeof(T).Name}-AsyncLazy", TimeSpan.FromMinutes(1));
    private Task<T>? _task;
    private int _isDisposed;

    /// <summary>
    /// 初始化 <see cref="AsyncLazy{T}"/> 实例,使用异步工厂委托。
    /// </summary>
    /// <param name="factory">用于异步创建值的工厂委托。</param>
    public AsyncLazy(Func<Task<T>> factory) {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>
    /// 初始化 <see cref="AsyncLazy{T}"/> 实例,使用同步工厂委托。
    /// </summary>
    /// <param name="factory">用于同步创建值的工厂委托。</param>
    public AsyncLazy(Func<T> factory) {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = () => Task.FromResult(factory());
    }

    /// <summary>
    /// 异步获取值 — 首次调用执行工厂委托并缓存,后续调用返回缓存结果。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    public async ValueTask<T> GetValueAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, typeof(AsyncLazy<T>));

        var task = Volatile.Read(ref _task);
        if (task is not null) {
            return await task.ConfigureAwait(false);
        }

        var releaser = await _gate.TryLockAsync(ct).ConfigureAwait(false)
            ?? throw new TimeoutException($"锁 '{_gate.Name}' 等待超时");
        using (releaser) {
            task = Volatile.Read(ref _task);
            if (task is not null) {
                return await task.ConfigureAwait(false);
            }

            task = _factory();
            Volatile.Write(ref _task, task);
            return await task.ConfigureAwait(false);
        }
    }

    /// <summary>获取值是否已创建。</summary>
    public bool IsValueCreated => Volatile.Read(ref _task) is not null;

    /// <summary>异步释放资源。</summary>
    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) {
            return ValueTask.CompletedTask;
        }

        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}