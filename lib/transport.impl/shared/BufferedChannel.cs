namespace JoinCode.Transport;

/// <summary>
/// 缓冲通道 — 线程安全的字符串行缓冲器，支持全量读取、增量读取和谓词检查
/// </summary>
public sealed class BufferedChannel : IDisposable
{
    private readonly List<string> _buffer = new();
    private readonly AsyncLock _lock = new();
    private int _consumedIndex;

    /// <summary>
    /// 添加一行到缓冲区
    /// </summary>
    /// <param name="line">要添加的行</param>
    /// <param name="ct">取消令牌</param>
    public async Task AddAsync(string line, CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        _buffer.Add(line);
    
    }

    /// <summary>
    /// 获取缓冲区全部内容（用换行符连接）
    /// </summary>
    /// <param name="lockTimeout">锁等待超时（仅用于异常消息）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用换行符连接的全部缓冲内容</returns>
    public async Task<string> GetAllAsync(TimeSpan lockTimeout, CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false)
            ?? throw new System.TimeoutException($"锁 '{_lock.Name}' BufferedChannel 等待超时 {lockTimeout}");

        return string.Join("\n", _buffer);
    
    }

    /// <summary>
    /// 获取自上次调用以来的增量内容（用换行符连接）
    /// </summary>
    /// <param name="lockTimeout">锁等待超时（仅用于异常消息）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>增量内容；无新内容时返回空字符串</returns>
    public async Task<string> GetIncrementalAsync(TimeSpan lockTimeout, CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false)
            ?? throw new System.TimeoutException($"锁 '{_lock.Name}' BufferedChannel 等待超时 {lockTimeout}");

        if (_consumedIndex >= _buffer.Count)
            return string.Empty;

        var result = string.Join("\n", _buffer[_consumedIndex..]);
        _consumedIndex = _buffer.Count;
        return result;
    
    }

    /// <summary>
    /// 清空缓冲区并重置消费索引
    /// </summary>
    /// <param name="lockTimeout">锁等待超时（仅用于异常消息）</param>
    /// <param name="ct">取消令牌</param>
    public async Task ClearAsync(TimeSpan lockTimeout, CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false)
            ?? throw new System.TimeoutException($"锁 '{_lock.Name}' BufferedChannel 等待超时 {lockTimeout}");

        _buffer.Clear();
        _consumedIndex = 0;
    
    }

    /// <summary>
    /// 对缓冲区全部内容（用换行符连接）应用谓词判断
    /// </summary>
    /// <param name="predicate">谓词函数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>谓词返回值</returns>
    public async Task<bool> TryPredicateAsync(Func<string, bool> predicate, CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        return predicate(string.Join("\n", _buffer));
    
    }

    /// <summary>释放内部锁资源</summary>
    public void Dispose() => _lock.Dispose();
}
