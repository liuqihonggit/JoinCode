
namespace JoinCode.Dream.Persistence;

/// <summary>
/// 做梦任务注册表接口
/// </summary>
public interface IDreamTaskRegistry
{
    /// <summary>
    /// 注册新的做梦任务
    /// </summary>
    Task<string> RegisterDreamTaskAsync(DreamTaskRegistrationRequest request, CancellationToken ct = default);

    /// <summary>
    /// 添加回合记录
    /// </summary>
    Task AddDreamTurnAsync(string taskId, DreamTurn turn, IReadOnlyList<string> touchedPaths, CancellationToken ct = default);

    /// <summary>
    /// 完成任务
    /// </summary>
    Task CompleteDreamTaskAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    /// 标记任务失败
    /// </summary>
    Task FailDreamTaskAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    /// 杀死任务
    /// </summary>
    Task KillDreamTaskAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    /// 获取任务状态
    /// </summary>
    Task<DreamTaskState?> GetTaskStateAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    /// 获取所有任务
    /// </summary>
    Task<IReadOnlyDictionary<string, DreamTaskState>> GetAllTasksAsync(CancellationToken ct = default);
}

/// <summary>
/// 做梦任务注册请求
/// </summary>
public sealed record DreamTaskRegistrationRequest(
    int SessionsReviewing,
    long PriorMtime,
    CancellationTokenSource AbortController);

/// <summary>
/// 内存中的做梦任务注册表实现
/// </summary>
[Register]
public sealed partial class InMemoryDreamTaskRegistry : IDreamTaskRegistry, IAsyncDisposable
{
    private readonly Dictionary<string, DreamTaskState> _tasks = new();
    private readonly AsyncLock _lock = new();

    public InMemoryDreamTaskRegistry()
    {
    }

    /// <inheritdoc />
    public async Task<string> RegisterDreamTaskAsync(DreamTaskRegistrationRequest request, CancellationToken ct = default)
    {
        var taskId = TaskIdGenerator.GenerateTaskId(TaskType.Dream);

        var task = new DreamTaskState
        {
            Id = taskId,
            Description = "dreaming",
            StartTime = DateTime.UtcNow,
            SessionsReviewing = request.SessionsReviewing,
            PriorMtime = request.PriorMtime,
            AbortController = request.AbortController,
            Status = DreamTaskStatus.Running,
            Phase = DreamPhase.Starting
        };

                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            _tasks[taskId] = task;
        }

        return taskId;
    }

    /// <inheritdoc />
    public async Task AddDreamTurnAsync(string taskId, DreamTurn turn, IReadOnlyList<string> touchedPaths, CancellationToken ct = default)
    {
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                task.AddTurn(turn, touchedPaths);
            }
        }
    }

    /// <inheritdoc />
    public async Task CompleteDreamTaskAsync(string taskId, CancellationToken ct = default)
    {
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                task.Complete();
            }
        }
    }

    /// <inheritdoc />
    public async Task FailDreamTaskAsync(string taskId, CancellationToken ct = default)
    {
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                task.Fail();
            }
        }
    }

    /// <inheritdoc />
    public async Task KillDreamTaskAsync(string taskId, CancellationToken ct = default)
    {
        DreamTaskState? task;
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            _tasks.TryGetValue(taskId, out task);
        }

        if (task == null || task.IsTerminal)
        {
            return;
        }

        task.Kill();

        // 如果有锁，回滚锁的mtime
        if (task.PriorMtime > 0)
        {
            // 这里需要通过外部传入ConsolidationLock来执行回滚
            // 简化处理：只记录需要回滚
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DreamTaskState?> GetTaskStateAsync(string taskId, CancellationToken ct = default)
    {
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            _tasks.TryGetValue(taskId, out var task);
            return task;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, DreamTaskState>> GetAllTasksAsync(CancellationToken ct = default)
    {
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            return new Dictionary<string, DreamTaskState>(_tasks);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _lock.Dispose();
    }
}
