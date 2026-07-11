namespace Core.Scheduling;

/// <summary>
/// 任务状态机 - 管理任务状态转换
/// </summary>
public sealed class TaskStateMachine
{
    private readonly Dictionary<TaskState, HashSet<TaskState>> _validTransitions;

    /// <summary>
    /// 当前状态
    /// </summary>
    public TaskState CurrentState { get; private set; }

    /// <summary>
    /// 状态变更事件
    /// </summary>
    public event EventHandler<TaskStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 创建任务状态机
    /// </summary>
    /// <param name="initialState">初始状态</param>
    public TaskStateMachine(TaskState initialState = TaskState.Pending)
    {
        CurrentState = initialState;
        _validTransitions = InitializeTransitions();
    }

    /// <summary>
    /// 尝试转换到目标状态
    /// </summary>
    /// <param name="targetState">目标状态</param>
    /// <returns>是否转换成功</returns>
    public bool TryTransitionTo(TaskState targetState)
    {
        if (!CanTransitionTo(targetState))
        {
            return false;
        }

        var previousState = CurrentState;
        CurrentState = targetState;

        StateChanged?.Invoke(this, new TaskStateChangedEventArgs(previousState, targetState));
        return true;
    }

    /// <summary>
    /// 强制转换到目标状态（不检查转换规则）
    /// </summary>
    /// <param name="targetState">目标状态</param>
    public void ForceTransitionTo(TaskState targetState)
    {
        var previousState = CurrentState;
        CurrentState = targetState;

        StateChanged?.Invoke(this, new TaskStateChangedEventArgs(previousState, targetState));
    }

    /// <summary>
    /// 检查是否可以转换到目标状态
    /// </summary>
    /// <param name="targetState">目标状态</param>
    /// <returns>是否可以转换</returns>
    public bool CanTransitionTo(TaskState targetState)
    {
        if (targetState == CurrentState)
        {
            return true;
        }

        return _validTransitions.TryGetValue(CurrentState, out var validTargets)
               && validTargets.Contains(targetState);
    }

    /// <summary>
    /// 获取所有有效的下一个状态
    /// </summary>
    /// <returns>有效的下一个状态集合</returns>
    public IReadOnlySet<TaskState> GetValidNextStates()
    {
        return _validTransitions.TryGetValue(CurrentState, out var validTargets)
            ? validTargets
            : new HashSet<TaskState>();
    }

    /// <summary>
    /// 检查任务是否处于终态
    /// </summary>
    /// <returns>是否是终态</returns>
    public bool IsTerminalState()
    {
        return CurrentState is TaskState.Completed
               or TaskState.Failed
               or TaskState.Cancelled
               or TaskState.Stopped;
    }

    /// <summary>
    /// 检查任务是否可以执行
    /// </summary>
    /// <returns>是否可以执行</returns>
    public bool CanExecute()
    {
        return CurrentState is TaskState.Pending or TaskState.WaitingForDependencies;
    }

    /// <summary>
    /// 初始化状态转换规则
    /// </summary>
    private Dictionary<TaskState, HashSet<TaskState>> InitializeTransitions()
    {
        return new Dictionary<TaskState, HashSet<TaskState>>
        {
            // 待处理状态可以转换到：等待依赖、进行中、已取消
            [TaskState.Pending] = new HashSet<TaskState>
            {
                TaskState.WaitingForDependencies,
                TaskState.InProgress,
                TaskState.Cancelled
            },

            // 等待依赖状态可以转换到：进行中、已取消
            [TaskState.WaitingForDependencies] = new HashSet<TaskState>
            {
                TaskState.InProgress,
                TaskState.Cancelled
            },

            // 进行中状态可以转换到：暂停、已完成、失败、已停止
            [TaskState.InProgress] = new HashSet<TaskState>
            {
                TaskState.Paused,
                TaskState.Completed,
                TaskState.Failed,
                TaskState.Stopped
            },

            // 暂停状态可以转换到：进行中、已取消
            [TaskState.Paused] = new HashSet<TaskState>
            {
                TaskState.InProgress,
                TaskState.Cancelled
            },

            // 终态不能再转换
            [TaskState.Completed] = new HashSet<TaskState>(),
            [TaskState.Failed] = new HashSet<TaskState>(),
            [TaskState.Cancelled] = new HashSet<TaskState>(),
            [TaskState.Stopped] = new HashSet<TaskState>()
        };
    }
}

/// <summary>
/// 任务状态变更事件参数
/// </summary>
public sealed class TaskStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 上一个状态
    /// </summary>
    public TaskState PreviousState { get; }

    /// <summary>
    /// 新状态
    /// </summary>
    public TaskState NewState { get; }

    /// <summary>
    /// 状态变更时间
    /// </summary>
    public DateTime ChangedAt { get; }

    public TaskStateChangedEventArgs(TaskState previousState, TaskState newState)
    {
        PreviousState = previousState;
        NewState = newState;
        ChangedAt = DateTime.UtcNow;
    }
}
