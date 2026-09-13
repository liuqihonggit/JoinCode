namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 文件快照，记录某一时刻的文件内容
/// </summary>
public sealed class FileSnapshot
{
    /// <summary>
    /// 快照内容
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// 快照生成时间戳
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// 文件路径（已规范化）
    /// </summary>
    public required string FilePath { get; init; }
}

/// <summary>
/// 文件历史服务，按文件路径维护内容快照列表，支持回滚到历史版本
/// </summary>
public sealed class FileHistoryService
{
    private readonly int _maxSnapshots;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    private readonly Dictionary<string, List<FileSnapshot>> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly AsyncLock _lock = new();

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="maxSnapshots">每个文件保留的最大快照数</param>
    /// <param name="clock">时钟服务（可选，默认使用系统时钟）</param>
    public FileHistoryService(IFileSystem fs, int maxSnapshots = 100, IClockService? clock = null)
    {
        _fs = fs;
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSnapshots, 1);
        _maxSnapshots = maxSnapshots;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 跟踪一次文件编辑，记录原始内容快照
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="originalContent">编辑前的原始内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>始终返回 true</returns>
    public async Task<bool> TrackEditAsync(string filePath, string originalContent, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        var normalizedPath = Path.GetFullPath(filePath);

        if (!_history.TryGetValue(normalizedPath, out var snapshots))
        {
            snapshots = [];
            _history[normalizedPath] = snapshots;
        }

        snapshots.Add(new FileSnapshot
        {
            Content = originalContent,
            Timestamp = _clock.GetUtcNow(),
            FilePath = normalizedPath
        });

        while (snapshots.Count > _maxSnapshots)
        {
            // 从尾部保留 _maxSnapshots 个，避免 RemoveAt(0) 的 O(n) 移动
            var removeCount = snapshots.Count - _maxSnapshots;
            snapshots.RemoveRange(0, removeCount);
        }

        return true;
    
    }

    /// <summary>
    /// 获取指定文件的所有快照
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>快照列表（按时间升序）；无记录时返回空列表</returns>
    public async Task<IReadOnlyList<FileSnapshot>> GetSnapshotsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        var normalizedPath = Path.GetFullPath(filePath);
        return _history.TryGetValue(normalizedPath, out var snapshots)
            ? snapshots.ToList()
            : [];
    
    }

    /// <summary>
    /// 回滚指定文件到指定快照版本
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="snapshotIndex">快照索引</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功恢复返回 true；文件无记录、索引越界或文件不存在返回 false</returns>
    public async Task<bool> RestoreAsync(string filePath, int snapshotIndex, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        var normalizedPath = Path.GetFullPath(filePath);

        if (!_history.TryGetValue(normalizedPath, out var snapshots))
            return false;

        if (snapshotIndex < 0 || snapshotIndex >= snapshots.Count)
            return false;

        var snapshot = snapshots[snapshotIndex];

        if (!_fs.FileExists(normalizedPath))
            return false;

        await _fs.WriteAllTextAsync(normalizedPath, snapshot.Content, cancellationToken).ConfigureAwait(false);
        return true;
    
    }

    /// <summary>
    /// 清除所有文件的历史快照
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        using var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        _history.Clear();
    
    }
}
