namespace Core.Utils;

public sealed class AsyncLazy<T> : IAsyncLazy<T> {
    private readonly Func<Task<T>> _factory;
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
    /// 异步获取值 — 首次调用执行工厂委托并缓存,后续调用返回缓存结果；工厂异常同样缓存。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    public async ValueTask<T> GetValueAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, typeof(AsyncLazy<T>));

        var task = Volatile.Read(ref _task);
        if (task is null) {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            task = Interlocked.CompareExchange(ref _task, completion.Task, null) ?? completion.Task;
            if (ReferenceEquals(task, completion.Task)) {
                _ = RunFactoryAsync(completion);
            }
        }

        return await task.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task RunFactoryAsync(TaskCompletionSource<T> completion) {
        try {
            completion.TrySetResult(await _factory().ConfigureAwait(false));
        } catch (OperationCanceledException ex) {
            completion.TrySetCanceled(ex.CancellationToken);
        } catch (Exception ex) {
            completion.TrySetException(ex);
        }
    }

    /// <summary>获取值是否已创建。</summary>
    public bool IsValueCreated => Volatile.Read(ref _task) is not null;

    /// <summary>异步释放资源。</summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) {
            return;
        }

        var task = Volatile.Read(ref _task);
        if (task is not null) await task.ConfigureAwait(false);
    }
}