namespace Core.Utils;

public sealed class AsyncLazy<T> : IAsyncLazy<T>
{
    private readonly Func<Task<T>> _factory;
    private readonly AsyncLock _gate = new($"{typeof(T).Name}-AsyncLazy", TimeSpan.FromMinutes(1));
    private Task<T>? _task;
    private int _isDisposed;

    public AsyncLazy(Func<Task<T>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public AsyncLazy(Func<T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = () => Task.FromResult(factory());
    }

    public async ValueTask<T> GetValueAsync(CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, typeof(AsyncLazy<T>));

        var task = Volatile.Read(ref _task);
        if (task is not null)
        {
            return await task.ConfigureAwait(false);
        }

        var releaser = await _gate.TryLockAsync(ct).ConfigureAwait(false)
            ?? throw new TimeoutException($"锁 '{_gate.Name}' 等待超时");
        using (releaser)
        {
            task = Volatile.Read(ref _task);
            if (task is not null)
            {
                return await task.ConfigureAwait(false);
            }

            task = _factory();
            Volatile.Write(ref _task, task);
            return await task.ConfigureAwait(false);
        }
    }

    public bool IsValueCreated => Volatile.Read(ref _task) is not null;

    public async ValueTask DisposeAsync()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _isDisposed))
        {
            return;
        }

        _gate.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
