namespace JoinCode.CodeIndex.Threading;

/// <summary>
/// ReaderWriterLockSlim 的 RAII scope 扩展 — 用 using var 管理锁生命周期,消除手写 try-finally + ExitXxxLock
/// 构造时进入锁,Dispose 时退出锁,保证配对不可遗漏
/// </summary>
public static class ReaderWriterLockSlimScope
{
    /// <summary>进入写锁,返回 IDisposable scope。用法:using var scope = _lock.EnterWriteScope();</summary>
    public static IDisposable EnterWriteScope(this ReaderWriterLockSlim lockSlim) => new WriteScope(lockSlim);

    /// <summary>进入读锁,返回 IDisposable scope。用法:using var scope = _lock.EnterReadScope();</summary>
    public static IDisposable EnterReadScope(this ReaderWriterLockSlim lockSlim) => new ReadScope(lockSlim);

    /// <summary>进入可升级读锁,返回 IDisposable scope。用法:using var scope = _lock.EnterUpgradeableReadScope();</summary>
    public static IDisposable EnterUpgradeableReadScope(this ReaderWriterLockSlim lockSlim) => new UpgradeableReadScope(lockSlim);

    private sealed class WriteScope : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        private int _disposed;
        public WriteScope(ReaderWriterLockSlim l) { _lock = l; _lock.EnterWriteLock(); }
        public void Dispose()
        {
            if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
            _lock.ExitWriteLock();
        }
    }

    private sealed class ReadScope : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        private int _disposed;
        public ReadScope(ReaderWriterLockSlim l) { _lock = l; _lock.EnterReadLock(); }
        public void Dispose()
        {
            if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
            _lock.ExitReadLock();
        }
    }

    private sealed class UpgradeableReadScope : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        private int _disposed;
        public UpgradeableReadScope(ReaderWriterLockSlim l) { _lock = l; _lock.EnterUpgradeableReadLock(); }
        public void Dispose()
        {
            if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
            _lock.ExitUpgradeableReadLock();
        }
    }
}
