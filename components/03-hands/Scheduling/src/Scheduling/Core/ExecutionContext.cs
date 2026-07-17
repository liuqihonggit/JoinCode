
namespace Core.Scheduling;

/// <summary>
/// 执行上下文 - 封装并行执行的状态
/// </summary>
internal sealed class ExecutionContext : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, byte> _completedTaskIds;
    private readonly List<Task> _runningTasks;
    private readonly AsyncLock _runningTasksLock = new();
    private int _isDisposed;

    public ExecutionContext(ExecutionOptions options,  CancellationToken cancellationToken)
    {
        Options = options;
        CancellationToken = cancellationToken;
        ConcurrencyLock = new SemaphoreSlim(options.MaxConcurrentTasks, options.MaxConcurrentTasks);
        _runningTasks = new List<Task>();
        _completedTaskIds = new ConcurrentDictionary<string, byte>();
    }

    public ExecutionOptions Options { get; }
    public CancellationToken CancellationToken { get; }
    public SemaphoreSlim ConcurrencyLock { get; }

    /// <summary>
    /// 添加运行中的任务 - 线程安全
    /// </summary>
    public async Task AddRunningTaskAsync(Task task)
    {
                using (await _runningTasksLock.LockAsync(CancellationToken).ConfigureAwait(false))
        {
            _runningTasks.Add(task);
        }
    }

    /// <summary>
    /// 获取运行中的任务列表快照 - 线程安全
    /// </summary>
    public async Task<List<Task>> GetRunningTasksSnapshotAsync()
    {
                using (await _runningTasksLock.LockAsync(CancellationToken).ConfigureAwait(false))
        {
            return _runningTasks.ToList();
        }
    }

    /// <summary>
    /// 清理已完成的任务 - 线程安全
    /// </summary>
    public async Task CleanupCompletedTasksAsync(CancellationToken cancellationToken = default)
    {
                using (await _runningTasksLock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            _runningTasks.RemoveAll(t => t.IsCompleted);
        }
    }

    /// <summary>
    /// 获取运行中任务数量 - 线程安全
    /// </summary>
    public async Task<int> GetRunningTaskCountAsync(CancellationToken cancellationToken = default)
    {
                using (await _runningTasksLock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            return _runningTasks.Count;
        }
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
    /// 获取已完成的任务ID集合
    /// </summary>
    public IReadOnlyCollection<string> GetCompletedTaskIds() => _completedTaskIds.Keys.ToList();

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        ConcurrencyLock.Dispose();
        _runningTasksLock.Dispose();
    }
}
