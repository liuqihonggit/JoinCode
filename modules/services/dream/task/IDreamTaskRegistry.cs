namespace JoinCode.Dream.Persistence;

/// <summary>
/// 做梦任务注册表接口
/// </summary>
public interface IDreamTaskRegistry : IRegistry {
    /// <summary>
    /// 注册新的做梦任务
    /// </summary>
    /// <param name="request">任务注册请求</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>任务 ID</returns>
    Task<string> RegisterDreamTaskAsync(DreamTaskRegistrationRequest request, CancellationToken ct = default);

    /// <summary>
    /// 添加回合记录
    /// </summary>
    /// <param name="taskId">任务 ID</param>
    /// <param name="turn">回合数据</param>
    /// <param name="touchedPaths">触及的文件路径列表</param>
    /// <param name="ct">取消令牌</param>
    Task AddDreamTurnAsync(string taskId, DreamTurn turn, IReadOnlyList<string> touchedPaths, CancellationToken ct = default);

    /// <summary>
    /// 完成任务
    /// </summary>
    /// <param name="taskId">任务 ID</param>
    /// <param name="ct">取消令牌</param>
    Task CompleteDreamTaskAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    /// 标记任务失败
    /// </summary>
    /// <param name="taskId">任务 ID</param>
    /// <param name="ct">取消令牌</param>
    Task FailDreamTaskAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    /// 杀死任务
    /// </summary>
    /// <param name="taskId">任务 ID</param>
    /// <param name="ct">取消令牌</param>
    Task KillDreamTaskAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    /// 获取任务状态
    /// </summary>
    /// <param name="taskId">任务 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>任务状态 — null 表示任务不存在</returns>
    Task<DreamTaskState?> GetTaskStateAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    /// 获取所有任务
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>任务 ID 到任务状态的字典</returns>
    Task<IReadOnlyDictionary<string, DreamTaskState>> GetAllTasksAsync(CancellationToken ct = default);

    /// <summary>设置会话隔离标识 — 持久化按 {storageDir}/{sessionId}/ 隔离</summary>
    /// <param name="sessionId">会话 ID</param>
    void SetSessionId(string sessionId);
}

/// <summary>
/// 做梦任务注册请求
/// </summary>
/// <param name="SessionsReviewing">审查中的会话数</param>
/// <param name="PriorMtime">先前修改时间戳</param>
/// <param name="AbortController">中止控制器</param>
/// <param name="RollbackLockAsync">kill 时锁回滚回调（TASK071 服务域-② — 对齐 TS DreamTask.kill
/// 自带回滚；null = 无锁场景跳过）</param>
public sealed record DreamTaskRegistrationRequest(
    int SessionsReviewing,
    long PriorMtime,
    CancellationTokenSource AbortController,
    Func<long, CancellationToken, Task>? RollbackLockAsync = null);

/// <summary>
/// 内存中的做梦任务注册表实现
/// </summary>
[Register(typeof(IDreamTaskRegistry), ServiceLifetime.Singleton)]
public sealed partial class InMemoryDreamTaskRegistry : IDreamTaskRegistry, IAsyncDisposable {
    private readonly Dictionary<string, DreamTaskState> _tasks = new();
    private readonly AsyncLock _lock = new();
    private int _disposed;

    /// <summary>
    /// 构造内存做梦任务注册表
    /// </summary>
    public InMemoryDreamTaskRegistry() {
    }

    /// <inheritdoc />
    public void SetSessionId(string sessionId) { }

    /// <inheritdoc />
    public async Task<string> RegisterDreamTaskAsync(DreamTaskRegistrationRequest request, CancellationToken ct = default) {
        var taskId = TaskIdGenerator.GenerateTaskId(TaskType.Dream);

        var task = new DreamTaskState {
            Id = taskId,
            Description = "dreaming",
            StartTime = DateTime.UtcNow,
            SessionsReviewing = request.SessionsReviewing,
            PriorMtime = request.PriorMtime,
            AbortController = request.AbortController,
            RollbackLockAsync = request.RollbackLockAsync,
            Status = DreamTaskStatus.Running,
            Phase = DreamPhase.Starting
        };

        using (await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            _tasks[taskId] = task;
        }

        return taskId;
    }

    /// <inheritdoc />
    public async Task AddDreamTurnAsync(string taskId, DreamTurn turn, IReadOnlyList<string> touchedPaths, CancellationToken ct = default) {
        using (await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            if (_tasks.TryGetValue(taskId, out var task)) {
                task.AddTurn(turn, touchedPaths);
            }
        }
    }

    /// <inheritdoc />
    public async Task CompleteDreamTaskAsync(string taskId, CancellationToken ct = default) {
        using (await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            if (_tasks.TryGetValue(taskId, out var task)) {
                task.Complete();
            }
        }
    }

    /// <inheritdoc />
    public async Task FailDreamTaskAsync(string taskId, CancellationToken ct = default) {
        using (await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            if (_tasks.TryGetValue(taskId, out var task)) {
                task.Fail();
            }
        }
    }

    /// <inheritdoc />
    public async Task KillDreamTaskAsync(string taskId, CancellationToken ct = default) {
        DreamTaskState? task;
        using (await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            _tasks.TryGetValue(taskId, out task);
        }

        if (task == null || task.IsTerminal) {
            return;
        }

        task.Kill();

        // TASK071 服务域-② — kill 回滚锁到 PriorMtime（时间门重新放行，kill 可安全重试；
        // 对齐 TS rollbackConsolidationLock — 旧实现此处是空操作注释）
        if (task.RollbackLockAsync is not null) {
            await task.RollbackLockAsync(task.PriorMtime, CancellationToken.None).ConfigureAwait(false);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DreamTaskState?> GetTaskStateAsync(string taskId, CancellationToken ct = default) {
        using (await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            _tasks.TryGetValue(taskId, out var task);
            return task;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, DreamTaskState>> GetAllTasksAsync(CancellationToken ct = default) {
        using (await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            return new Dictionary<string, DreamTaskState>(_tasks);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lock.Dispose();
    }
}