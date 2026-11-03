
namespace Core.Scheduling;

/// <summary>
/// 执行上下文 - 封装并行执行的状态
/// </summary>
internal sealed class ExecutionContext : IAsyncDisposable {
    private readonly ConcurrentDictionary<string, byte> _completedTaskIds;
    private readonly ConcurrentDictionary<Task, byte> _runningTasks = new();
    private int _isDisposed;

    /// <summary>构造执行上下文。</summary>
    /// <param name="options">执行选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    public ExecutionContext(ExecutionOptions options, CancellationToken cancellationToken) {
        Options = options;
        CancellationToken = cancellationToken;
        ConcurrencyLock = new AsyncLock(nameof(ExecutionContext) + ".Concurrency", options.MaxConcurrentTasks, options.MaxConcurrentTasks);
        _completedTaskIds = new ConcurrentDictionary<string, byte>();
    }

    /// <summary>获取执行选项。</summary>
    public ExecutionOptions Options { get; }
    /// <summary>获取取消令牌。</summary>
    public CancellationToken CancellationToken { get; }
    /// <summary>获取并发锁。</summary>
    public AsyncLock ConcurrencyLock { get; }

    /// <summary>
    /// 添加运行中的任务 - 线程安全（ConcurrentDictionary 无锁）
    /// </summary>
    public Task AddRunningTaskAsync(Task task) {
        _runningTasks.TryAdd(task, 0);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取运行中的任务列表快照 - 线程安全（ConcurrentDictionary 无锁）
    /// </summary>
    public Task<List<Task>> GetRunningTasksSnapshotAsync() {
        return Task.FromResult(_runningTasks.Keys.ToList());
    }

    /// <summary>
    /// 清理已完成的任务 - 线程安全（ConcurrentDictionary 遍历+TryRemove 无锁）
    /// </summary>
    public Task CleanupCompletedTasksAsync(CancellationToken cancellationToken = default) {
        foreach (var kvp in _runningTasks) {
            if (kvp.Key.IsCompleted)
                _runningTasks.TryRemove(kvp.Key, out _);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取运行中任务数量 - 线程安全（ConcurrentDictionary.Count 无锁）
    /// </summary>
    public Task<int> GetRunningTaskCountAsync(CancellationToken cancellationToken = default) {
        return Task.FromResult(_runningTasks.Count);
    }

    /// <summary>
    /// 尝试将任务标记为已完成，返回是否成功标记
    /// </summary>
    public bool TryMarkCompleted(string taskId) => _completedTaskIds.TryAdd(taskId, 0);

    /// <summary>
    /// 检查任务是否已完成
    /// </summary>
    public bool IsCompleted(string taskId) => _completedTaskIds.ContainsKey(taskId);

    /// <summary>
    /// 获取已完成任务ID的快照拷贝
    /// </summary>
    public string[] GetCompletedTaskIds() => _completedTaskIds.Keys.ToArray();

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) {
            return;
        }

        var snapshot = _runningTasks.Keys.ToArray();
        await Task.WhenAll(snapshot).ConfigureAwait(false);

        ConcurrencyLock.Dispose();
    }
}
