namespace Core.Utils;

public abstract class DisposableBase : IDisposable
{
    private int _isDisposed;

    public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    protected void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;
        DisposeCore(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void DisposeCore(bool disposing) { }
}

public abstract class AsyncDisposableBase : IAsyncDisposable, IDisposable
{
    private int _isDisposed;

    public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    protected void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;
        DisposeCore(true);
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return ValueTask.CompletedTask;
        DisposeCore(true);
        GC.SuppressFinalize(this);
        return DisposeAsyncCore();
    }

    protected virtual void DisposeCore(bool disposing) { }

    protected virtual ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
}
