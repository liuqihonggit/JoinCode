namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 非托管资源句柄 — using 模式自动注销并释放
/// <para>从 UnmanagedResourceTable.Register 获得,Dispose 时从表中移除并释放 SafeHandle</para>
/// </summary>
public sealed class UnmanagedResourceHandle : IDisposable
{
    private readonly UnmanagedResourceTable _table;
    private readonly string _key;
    private bool _disposed;

    internal UnmanagedResourceHandle(UnmanagedResourceTable table, string key)
    {
        _table = table;
        _key = key;
    }

    /// <summary>注销并释放资源 — 幂等</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _table.ReleaseInternal(_key);
    }
}
