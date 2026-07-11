namespace Core.Utils;

public sealed class AsyncLazy<T> : IAsyncLazy<T>
{
    private readonly Func<Task<T>> _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
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
        ObjectDisposedException.ThrowIf(_isDisposed == 1, typeof(AsyncLazy<T>));

        var task = Volatile.Read(ref _task);
        if (task is not null)
        {
            return await task.ConfigureAwait(false);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
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
        finally
        {
            _gate.Release();
        }
    }

    public bool IsValueCreated => Volatile.Read(ref _task) is not null;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        _gate.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
