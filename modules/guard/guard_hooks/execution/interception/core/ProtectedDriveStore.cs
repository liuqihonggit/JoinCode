namespace Core.Hooks.Execution.Interception;

/// <summary>
/// 受保护盘号存储默认实现 — volatile 双变量原子切换（对齐 IToolHealthMonitor.UpdateBlacklist 模式）。
/// <para>ADR 0123</para>
/// </summary>
[Register(typeof(IProtectedDriveStore), ServiceLifetime.Singleton)]
public sealed class ProtectedDriveStore : IProtectedDriveStore {
    private volatile FrozenSet<string> _drives = FrozenSet<string>.Empty;

    /// <inheritdoc/>
    public FrozenSet<string> ProtectedDrives => _drives;

    /// <inheritdoc/>
    public void Update(FrozenSet<string> drives)
        => Interlocked.Exchange(ref _drives, drives);
}
