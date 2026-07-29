
namespace JoinCode.Dream.Persistence;

/// <summary>
/// 持久化的做梦任务注册表
/// </summary>
[Register]
public sealed partial class PersistentDreamTaskRegistry : IDreamTaskRegistry, IAsyncDisposable
{
    private readonly IDreamTaskPersistence _persistence;
    [Inject] private readonly ILogger<PersistentDreamTaskRegistry>? _logger;
    private readonly AsyncLock _lock = new();

    // 内存缓存（活跃任务）
    private readonly Dictionary<string, DreamTaskState> _activeTasks = new();

    public PersistentDreamTaskRegistry(
        IDreamTaskPersistence persistence,
        
        ILogger<PersistentDreamTaskRegistry>? logger = null)
    {
        _persistence = persistence;
        _logger = logger;
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
            _activeTasks[taskId] = task;
        }

        // 异步保存
        await _persistence.SaveAsync(task, ct).ConfigureAwait(false);

        _logger?.LogInformation("[PersistentDreamTaskRegistry] 注册任务 {TaskId}", taskId);
        return taskId;
    }

    /// <inheritdoc />
    public async Task AddDreamTurnAsync(string taskId, DreamTurn turn, IReadOnlyList<string> touchedPaths, CancellationToken ct = default)
    {
        DreamTaskState? task;
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            if (!_activeTasks.TryGetValue(taskId, out task))
            {
                return;
            }

            task.AddTurn(turn, touchedPaths);
        }

        // 异步保存（在锁外）
        if (task != null)
        {
            await _persistence.SaveAsync(task, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task CompleteDreamTaskAsync(string taskId, CancellationToken ct = default)
    {
        DreamTaskState? task;
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            if (!_activeTasks.TryGetValue(taskId, out task))
            {
                return;
            }

            task.Complete();

            // 从活跃缓存移除
            _activeTasks.Remove(taskId);
        }

        // 异步保存（在锁外）
        if (task != null)
        {
            await _persistence.SaveAsync(task, ct).ConfigureAwait(false);
        }

        _logger?.LogInformation("[PersistentDreamTaskRegistry] 完成任务 {TaskId}", taskId);
    }

    /// <inheritdoc />
    public async Task FailDreamTaskAsync(string taskId, CancellationToken ct = default)
    {
        DreamTaskState? task;
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            if (!_activeTasks.TryGetValue(taskId, out task))
            {
                return;
            }

            task.Fail();

            // 从活跃缓存移除
            _activeTasks.Remove(taskId);
        }

        // 异步保存（在锁外）
        if (task != null)
        {
            await _persistence.SaveAsync(task, ct).ConfigureAwait(false);
        }

        _logger?.LogWarning("[PersistentDreamTaskRegistry] 任务失败 {TaskId}", taskId);
    }

    /// <inheritdoc />
    public async Task KillDreamTaskAsync(string taskId, CancellationToken ct = default)
    {
        DreamTaskState? task;

                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            _activeTasks.TryGetValue(taskId, out task);
        }

        if (task == null || task.IsTerminal)
        {
            return;
        }

        task.Kill();

                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            _activeTasks.Remove(taskId);
        }

        await _persistence.SaveAsync(task, ct).ConfigureAwait(false);

        _logger?.LogInformation("[PersistentDreamTaskRegistry] 杀死任务 {TaskId}", taskId);
    }

    /// <inheritdoc />
    public async Task<DreamTaskState?> GetTaskStateAsync(string taskId, CancellationToken ct = default)
    {
        // 先从内存缓存查找
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            if (_activeTasks.TryGetValue(taskId, out var activeTask))
            {
                return activeTask;
            }
        }

        // 从持久化加载
        return await _persistence.LoadAsync(taskId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, DreamTaskState>> GetAllTasksAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, DreamTaskState>();

        // 添加活跃任务
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            foreach (var (id, task) in _activeTasks)
            {
                result[id] = task;
            }
        }

        // 添加历史任务（从持久化加载）
        var persistedTasks = await _persistence.LoadAllAsync(ct).ConfigureAwait(false);
        foreach (var task in persistedTasks)
        {
            if (!result.ContainsKey(task.Id))
            {
                result[task.Id] = task;
            }
        }

        return result;
    }

    /// <summary>
    /// 加载活跃任务（服务启动时调用）
    /// </summary>
    public async Task LoadActiveTasksAsync(CancellationToken ct = default)
    {
        var allTasks = await _persistence.LoadAllAsync(ct).ConfigureAwait(false);
        var activeTasks = allTasks.Where(t => !t.IsTerminal).ToList();

                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            foreach (var task in activeTasks)
            {
                _activeTasks[task.Id] = task;
            }
        }

        _logger?.LogInformation(
            "[PersistentDreamTaskRegistry] 加载了 {Count} 个活跃任务",
            activeTasks.Count);
    }

    /// <summary>
    /// 清理已完成的任务
    /// </summary>
    public async Task CleanupAsync(int keepCount = 10)
    {
        await _persistence.CleanupCompletedAsync(keepCount).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _lock.Dispose();
    }
}
