namespace Core.Utils;

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
