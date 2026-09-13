
namespace Core.Agents.Coordinator;

/// <summary>
/// Agent状态机 - 管理Agent的生命周期和状态转换
/// <para>内部复用 StateMachine&lt;TState&gt; 基础设施,消除手写 switch 转换表/锁/事件重复逻辑</para>
/// </summary>
[Register(typeof(AgentStateMachine), ServiceLifetime.Singleton)]
public sealed partial class AgentStateMachine 
{
    private static readonly FrozenDictionary<TaskExecutionStatus, FrozenSet<TaskExecutionStatus>> Transitions = CreateTransitionTable();

    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, AgentStateContext> _states;
    private readonly IClockService _clock;

    /// <summary>Agent 状态变更事件，参数携带 Agent ID 与新旧状态</summary>
    internal event EventHandler<AgentStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 构造 Agent 状态机实例
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="clock">可选时钟服务，缺省时使用系统时钟</param>
    public AgentStateMachine(ILogger? logger = null, IClockService? clock = null)
    {
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _states = new ConcurrentDictionary<string, AgentStateContext>();
    }

    /// <summary>
    /// 注册Agent状态
    /// </summary>
    public void RegisterAgent(string agentId, string task, SubAgentOptions? options = null)
    {
        var now = _clock.GetUtcNow();
        var context = new AgentStateContext(agentId, task, options, now, _clock);
        _states[agentId] = context;
        _logger?.LogDebug("[AgentStateMachine] Agent {AgentId} 已注册，初始状态: {State}", agentId, context.CurrentState);
    }

    /// <summary>
    /// 尝试转换状态
    /// </summary>
    public async ValueTask<bool> TryTransitionAsync(string agentId, TaskExecutionStatus newState, string? reason = null, CancellationToken ct = default)
    {
        if (!_states.TryGetValue(agentId, out var context))
        {
            _logger?.LogWarning("[AgentStateMachine] Agent {AgentId} 未找到", agentId);
            return false;
        }

        var lk = context.Lock;
        using var guard = await lk.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时");
        if (!context.StateMachine.TryTransitionTo(newState))
        {
            _logger?.LogWarning("[AgentStateMachine] Agent {AgentId} 无法从 {CurrentState} 转换到 {NewState}",
                agentId, context.CurrentState, newState);
            return false;
        }

        var oldState = context.LastTransitionFrom;
        var now = _clock.GetUtcNow();
        context.LastTransitionTime = now;
        context.TransitionHistory.Add(new StateTransition(oldState, newState, now, reason));

        // 更新特定状态的时间戳
        switch (newState)
        {
            case TaskExecutionStatus.Running:
                context.StartedAt = now;
                break;
            case TaskExecutionStatus.Completed:
            case TaskExecutionStatus.Failed:
            case TaskExecutionStatus.Cancelled:
                context.CompletedAt = now;
                break;
        }

        _logger?.LogInformation("[AgentStateMachine] Agent {AgentId} 状态转换: {OldState} -> {NewState}",
            agentId, oldState, newState);

        StateChanged?.Invoke(this, new AgentStateChangedEventArgs(agentId, oldState, newState));

        return true;
    }

    /// <summary>
    /// 获取Agent当前状态
    /// </summary>
    public TaskExecutionStatus? GetState(string agentId)
    {
        return _states.TryGetValue(agentId, out var context) ? context.CurrentState : null;
    }

    /// <summary>
    /// 获取Agent状态上下文
    /// </summary>
    public AgentStateContext? GetContext(string agentId)
    {
        return _states.TryGetValue(agentId, out var context) ? context : null;
    }

    /// <summary>
    /// 获取所有Agent状态
    /// </summary>
    public IReadOnlyDictionary<string, TaskExecutionStatus> GetAllStates()
    {
        return _states.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.CurrentState);
    }

    /// <summary>
    /// 检查Agent是否处于最终状态
    /// </summary>
    public bool IsInFinalState(string agentId)
    {
        var state = GetState(agentId);
        return state.HasValue && state.Value.IsTerminal();
    }

    /// <summary>
    /// 获取处于特定状态的Agent列表
    /// </summary>
    public IEnumerable<string> GetAgentsInState(TaskExecutionStatus state)
    {
        return _states
            .Where(kvp => kvp.Value.CurrentState == state)
            .Select(kvp => kvp.Key);
    }

    /// <summary>
    /// 等待所有Agent进入最终状态
    /// </summary>
    public async Task WaitAllFinalAsync(CancellationToken cancellationToken = default)
    {
        while (_states.Values.Any(c => !IsFinalState(c.CurrentState)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 获取状态报告
    /// </summary>
    public AgentStateReport GetReport()
    {
        var states = _states.Values;
        return new AgentStateReport
        {
            TotalAgents = _states.Count,
            PendingCount = states.Count(c => c.CurrentState == TaskExecutionStatus.Pending),
            RunningCount = states.Count(c => c.CurrentState == TaskExecutionStatus.Running),
            PausedCount = states.Count(c => c.CurrentState == TaskExecutionStatus.Paused),
            CompletedCount = states.Count(c => c.CurrentState == TaskExecutionStatus.Completed),
            FailedCount = states.Count(c => c.CurrentState == TaskExecutionStatus.Failed),
            CancelledCount = states.Count(c => c.CurrentState == TaskExecutionStatus.Cancelled),
            Agents = states.Select(c => new AgentStateInfo
            {
                AgentId = c.AgentId,
                Task = c.Task,
                CurrentState = c.CurrentState,
                StartedAt = c.StartedAt,
                CompletedAt = c.CompletedAt,
                ExecutionTimeMs = c.CompletedAt.HasValue && c.StartedAt.HasValue
                    ? (long)(c.CompletedAt.Value - c.StartedAt.Value).TotalMilliseconds
                    : c.StartedAt.HasValue
                        ? (long)(_clock.GetUtcNow() - c.StartedAt.Value).TotalMilliseconds
                        : null,
                Options = c.Options,
                Progress = c.Options?.ProgressTracker?.ToProgress(),
            }).ToList()
        };
    }

    /// <summary>
    /// 移除Agent状态
    /// </summary>
    public bool RemoveAgent(string agentId)
    {
        return _states.TryRemove(agentId, out _);
    }

    private static bool IsFinalState(TaskExecutionStatus state)
    {
        return state.IsTerminal();
    }

    /// <summary>获取任务执行状态转换表（只读快照），键为当前状态，值为可转换到的目标状态集合</summary>
    /// <returns>状态转换表的冻结字典快照</returns>
    internal static FrozenDictionary<TaskExecutionStatus, FrozenSet<TaskExecutionStatus>> GetTransitions() => Transitions;

    private static FrozenDictionary<TaskExecutionStatus, FrozenSet<TaskExecutionStatus>> CreateTransitionTable()
    {
        return new Dictionary<TaskExecutionStatus, FrozenSet<TaskExecutionStatus>>
        {
            [TaskExecutionStatus.Pending] = FrozenSet.Create(
                TaskExecutionStatus.Running, TaskExecutionStatus.Cancelled),
            [TaskExecutionStatus.Running] = FrozenSet.Create(
                TaskExecutionStatus.Paused, TaskExecutionStatus.Completed,
                TaskExecutionStatus.Failed, TaskExecutionStatus.Cancelled),
            [TaskExecutionStatus.Paused] = FrozenSet.Create(
                TaskExecutionStatus.Running, TaskExecutionStatus.Cancelled),
            [TaskExecutionStatus.Completed] = FrozenSet.Create(
                TaskExecutionStatus.Running), // 允许重试
            [TaskExecutionStatus.Failed] = FrozenSet.Create(
                TaskExecutionStatus.Running, TaskExecutionStatus.Cancelled), // 允许重试
            [TaskExecutionStatus.Cancelled] = FrozenSet<TaskExecutionStatus>.Empty, // 终止状态
            [TaskExecutionStatus.WaitingForDependency] = FrozenSet<TaskExecutionStatus>.Empty,
            [TaskExecutionStatus.Ready] = FrozenSet<TaskExecutionStatus>.Empty,
        }.ToFrozenDictionary();
    }
}

/// <summary>
/// Agent状态上下文
/// </summary>
public sealed class AgentStateContext : IAsyncDisposable
{
    private readonly StateMachine<TaskExecutionStatus> _stateMachine;
    private int _disposed;

    /// <summary>Agent 标识</summary>
    public string AgentId { get; }
    /// <summary>任务描述</summary>
    public string Task { get; }
    /// <summary>子 Agent 选项</summary>
    public SubAgentOptions Options { get; }
    /// <summary>当前执行状态</summary>
    public TaskExecutionStatus CurrentState => _stateMachine.CurrentState;
    /// <summary>上下文创建时间</summary>
    public DateTime CreatedAt { get; }
    /// <summary>Agent 开始执行时间</summary>
    public DateTime? StartedAt { get; set; }
    /// <summary>Agent 完成时间（成功/失败/取消）</summary>
    public DateTime? CompletedAt { get; set; }
    /// <summary>最近一次状态转换时间</summary>
    public DateTime LastTransitionTime { get; set; }
    /// <summary>状态转换历史记录列表</summary>
    public List<StateTransition> TransitionHistory { get; }
    /// <summary>异步锁，保护状态转换的并发安全</summary>
    public AsyncLock Lock { get; }
    /// <summary>最近一次状态转换的源状态</summary>
    internal TaskExecutionStatus LastTransitionFrom { get; private set; }

    /// <summary>暴露给同程序集内部使用的状态机引用</summary>
    internal StateMachine<TaskExecutionStatus> StateMachine => _stateMachine;

    /// <summary>
    /// 构造 Agent 状态上下文实例
    /// </summary>
    /// <param name="agentId">Agent 标识</param>
    /// <param name="task">任务描述</param>
    /// <param name="options">子 Agent 选项，缺省时使用默认选项</param>
    /// <param name="createdAt">上下文创建时间</param>
    /// <param name="clock">可选时钟服务</param>
    public AgentStateContext(string agentId, string task, SubAgentOptions? options, DateTime createdAt, IClockService? clock = null)
    {
        AgentId = agentId;
        Task = task;
        Options = options ?? new SubAgentOptions();
        CreatedAt = createdAt;
        LastTransitionTime = createdAt;
        LastTransitionFrom = TaskExecutionStatus.Pending;
        TransitionHistory = new List<StateTransition>();
        Lock = new AsyncLock(nameof(AgentStateContext));
        _stateMachine = new StateMachine<TaskExecutionStatus>(
            AgentStateMachine.GetTransitions(), TaskExecutionStatus.Pending, clock);
        _stateMachine.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, StateChangedEventArgs<TaskExecutionStatus> e)
    {
        LastTransitionFrom = e.OldState;
    }

    /// <summary>
    /// 异步释放上下文，释放内部锁资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Lock.Dispose();
    }
}

/// <summary>
/// 状态转换记录
/// </summary>
public sealed record StateTransition(
    TaskExecutionStatus FromState,
    TaskExecutionStatus ToState,
    DateTime Timestamp,
    string? Reason);

/// <summary>
/// Agent 状态变更事件参数
/// </summary>
public sealed class AgentStateChangedEventArgs(string agentId, TaskExecutionStatus oldState, TaskExecutionStatus newState) : EventArgs
{
    /// <summary>Agent 标识</summary>
    public string AgentId { get; } = agentId;
    /// <summary>变更前的状态</summary>
    public TaskExecutionStatus OldState { get; } = oldState;
    /// <summary>变更后的状态</summary>
    public TaskExecutionStatus NewState { get; } = newState;
}

