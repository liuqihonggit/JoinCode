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

public static class DisposableHelper
{
    public static bool TryMarkDisposed(ref int isDisposed)
        => Interlocked.Exchange(ref isDisposed, 1) == 0;

    public static bool IsDisposed(ref int isDisposed)
        => Volatile.Read(ref isDisposed) != 0;

    public static void ThrowIfDisposed(ref int isDisposed, object instance)
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, instance);

    public static bool TryMarkDisposed(ref bool isDisposed)
    {
        if (isDisposed) return false;
        isDisposed = true;
        return true;
    }

    public static bool IsDisposed(ref bool isDisposed)
        => isDisposed;

    public static void ThrowIfDisposed(ref bool isDisposed, object instance)
        => ObjectDisposedException.ThrowIf(isDisposed, instance);
}
