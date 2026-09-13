namespace Core.Scheduling;

/// <summary>
/// 任务状态机 — 基于预定义的状态转移表管理任务生命周期状态转换
/// </summary>
public sealed class TaskStateMachine
{
    private static readonly FrozenDictionary<TaskState, FrozenSet<TaskState>> Transitions = CreateTransitionTable();

    private readonly StateMachine<TaskState> _stateMachine;

    /// <summary>
    /// 初始化任务状态机实例
    /// </summary>
    /// <param name="initialState">初始状态,默认为 Pending</param>
    public TaskStateMachine(TaskState initialState = TaskState.Pending)
    {
        _stateMachine = new StateMachine<TaskState>(Transitions, initialState);
        _stateMachine.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// 获取当前任务状态
    /// </summary>
    public TaskState CurrentState => _stateMachine.CurrentState;

    /// <summary>
    /// 状态变更事件 — 当任务状态发生转换时触发
    /// </summary>
    public event EventHandler<StateChangedEventArgs<TaskState>>? StateChanged;

    /// <summary>
    /// 尝试转换到目标状态,若转换合法则执行并返回 true,否则返回 false
    /// </summary>
    /// <param name="targetState">目标状态</param>
    /// <returns>转换成功返回 true,失败返回 false</returns>
    public bool TryTransitionTo(TaskState targetState) => _stateMachine.TryTransitionTo(targetState);

    /// <summary>
    /// 强制转换到目标状态,跳过合法性校验
    /// </summary>
    /// <param name="targetState">目标状态</param>
    public void ForceTransitionTo(TaskState targetState) => _stateMachine.ForceTransitionTo(targetState);

    /// <summary>
    /// 检查是否可以转换到目标状态
    /// </summary>
    /// <param name="targetState">目标状态</param>
    /// <returns>可转换返回 true,不可转换返回 false</returns>
    public bool CanTransitionTo(TaskState targetState) => _stateMachine.CanTransitionTo(targetState);

    /// <summary>
    /// 获取当前状态下所有合法的下一状态集合
    /// </summary>
    /// <returns>合法下一状态的只读集合</returns>
    public IReadOnlySet<TaskState> GetValidNextStates() => _stateMachine.GetValidNextStates();

    /// <summary>
    /// 判断当前状态是否为终态(Completed/Failed/Cancelled/Stopped)
    /// </summary>
    /// <returns>是终态返回 true,否则返回 false</returns>
    public bool IsTerminalState()
    {
        return CurrentState is TaskState.Completed
               or TaskState.Failed
               or TaskState.Cancelled
               or TaskState.Stopped;
    }

    /// <summary>
    /// 判断当前状态是否可以执行(Pending 或 WaitingForDependency)
    /// </summary>
    /// <returns>可执行返回 true,否则返回 false</returns>
    public bool CanExecute()
    {
        return CurrentState is TaskState.Pending or TaskState.WaitingForDependency;
    }

    private void OnStateChanged(object? sender, StateChangedEventArgs<TaskState> e)
    {
        StateChanged?.Invoke(this, e);
    }

    private static FrozenDictionary<TaskState, FrozenSet<TaskState>> CreateTransitionTable()
    {
        return new Dictionary<TaskState, FrozenSet<TaskState>>
        {
            [TaskState.Pending] = new HashSet<TaskState>
            {
                TaskState.WaitingForDependency,
                TaskState.Running,
                TaskState.Cancelled
            }.ToFrozenSet(),

            [TaskState.WaitingForDependency] = new HashSet<TaskState>
            {
                TaskState.Running,
                TaskState.Cancelled
            }.ToFrozenSet(),

            [TaskState.Running] = new HashSet<TaskState>
            {
                TaskState.Paused,
                TaskState.Completed,
                TaskState.Failed,
                TaskState.Stopped
            }.ToFrozenSet(),

            [TaskState.Paused] = new HashSet<TaskState>
            {
                TaskState.Running,
                TaskState.Cancelled
            }.ToFrozenSet(),

            [TaskState.Completed] = new HashSet<TaskState>().ToFrozenSet(),
            [TaskState.Failed] = new HashSet<TaskState>().ToFrozenSet(),
            [TaskState.Cancelled] = new HashSet<TaskState>().ToFrozenSet(),
            [TaskState.Stopped] = new HashSet<TaskState>().ToFrozenSet()
        }.ToFrozenDictionary();
    }
}
