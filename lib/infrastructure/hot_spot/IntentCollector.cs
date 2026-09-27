namespace Infrastructure.HotSpot;

/// <summary>
/// 意图收集器实现 — ConcurrentDictionary + per-key lock 线程安全
/// 按 filePath 索引意图，支持多 Worker 并发上报和清理
/// </summary>
[Register(typeof(IIntentCollector), ServiceLifetime.Singleton)]
public sealed class IntentCollector : IIntentCollector {
    private ImmutableHamT<string, ImmutableList<FileModifyIntent>> _intentsByFile = ImmutableHamT<string, ImmutableList<FileModifyIntent>>.Empty;
    private ImmutableHamT<string, AsyncLock> _locks = ImmutableHamT<string, AsyncLock>.Empty;
    private readonly IClockService _clock;

    /// <summary>
    /// 构造意图收集器
    /// </summary>
    /// <param name="clock">时钟服务，默认使用系统时钟</param>
    public IntentCollector(IClockService? clock = null) {
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 异步上报一组文件修改意图
    /// </summary>
    /// <param name="workerId">上报的 Worker ID</param>
    /// <param name="intents">文件修改意图列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task ReportAsync(string workerId, IReadOnlyList<FileModifyIntent> intents, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentNullException.ThrowIfNull(intents);

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var intent in intents) {
            cancellationToken.ThrowIfCancellationRequested();
            var key = NormalizePath(intent.FilePath);
            var lk = GetLock(key);
            using (lk.TryLock() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
                ImmutableInterlocked.Update(ref _intentsByFile, d => {
                    var list = d.GetValueOrDefault(key, ImmutableList<FileModifyIntent>.Empty);
                    return d.SetItem(key, list.Add(intent));
                });
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取指定文件的所有修改意图
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>该文件的修改意图列表，无记录则返回空列表</returns>
    public IReadOnlyList<FileModifyIntent> GetIntents(string filePath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var key = NormalizePath(filePath);
        var lk = GetLock(key);
        using (lk.TryLock() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
            if (Volatile.Read(ref _intentsByFile).TryGetValue(key, out var list))
                return [.. list];
        }
        return [];
    }

    /// <summary>
    /// 获取所有文件的所有修改意图
    /// </summary>
    /// <returns>全部修改意图列表</returns>
    public IReadOnlyList<FileModifyIntent> GetAllIntents() {
        var all = new List<FileModifyIntent>();
        foreach (var kvp in Volatile.Read(ref _intentsByFile)) {
            var lk = GetLock(kvp.Key);
            using (lk.TryLock() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
                all.AddRange(kvp.Value);
            }
        }
        return all;
    }

    /// <summary>
    /// 异步移除指定 Worker 的所有意图记录
    /// </summary>
    /// <param name="workerId">Worker ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task RemoveWorkerAsync(string workerId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var kvp in Volatile.Read(ref _intentsByFile)) {
            cancellationToken.ThrowIfCancellationRequested();
            var lk = GetLock(kvp.Key);
            using (lk.TryLock() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
                ImmutableInterlocked.Update(ref _intentsByFile, d => {
                    if (!d.TryGetValue(kvp.Key, out var list)) return d;
                    var next = list.RemoveAll(x => x.WorkerId == workerId);
                    return next.IsEmpty ? d.Remove(kvp.Key) : d.SetItem(kvp.Key, next);
                });
            }
        }

        return Task.CompletedTask;
    }

    private AsyncLock GetLock(string filePath) {
        var snapshot = Volatile.Read(ref _locks);
        if (snapshot.TryGetValue(filePath, out var existing))
            return existing;

        var newLock = new AsyncLock(nameof(IntentCollector));
        ImmutableInterlocked.Update(ref _locks, d => d.ContainsKey(filePath) ? d : d.Add(filePath, newLock));
        return Volatile.Read(ref _locks)[filePath];
    }

    private static string NormalizePath(string filePath) => filePath.Replace('\\', '/');
}