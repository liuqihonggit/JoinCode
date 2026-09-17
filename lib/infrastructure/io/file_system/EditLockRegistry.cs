namespace IO.FileSystem;

/// <summary>
/// 文件编辑锁注册表 — 按规范化路径缓存 per-file AsyncLock，
/// 同一文件 <c>EditFileAsync</c> 串行化，不同文件并行。
/// 锁按规范化路径缓存，生命周期与宿主 FileSystem 相同。
/// </summary>
internal sealed class EditLockRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, AsyncLock> _locks = new();
    private volatile bool _disposed;

    /// <summary>
    /// 获取或创建指定规范化路径的编辑锁。
    /// </summary>
    /// <param name="normalizedPath">已规范化的文件路径（调用方负责规范化）。</param>
    /// <returns>该路径对应的 AsyncLock 实例（幂等，同路径返回同实例）。</returns>
    public AsyncLock GetOrAdd(string normalizedPath)
        => _locks.GetOrAdd(normalizedPath, p => new AsyncLock($"EditFile:{p}"));

    /// <summary>
    /// 释放所有缓存的编辑锁并清空注册表。幂等，多次调用安全。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var kvp in _locks)
            kvp.Value.Dispose();
        _locks.Clear();
    }
}
