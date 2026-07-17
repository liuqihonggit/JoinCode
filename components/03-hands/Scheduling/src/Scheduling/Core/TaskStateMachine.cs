namespace Core.Scheduling;

public sealed class TaskStateMachine
{
    private static readonly FrozenDictionary<TaskState, FrozenSet<TaskState>> Transitions = CreateTransitionTable();

    private readonly StateMachine<TaskState> _stateMachine;

    public TaskStateMachine(TaskState initialState = TaskState.Pending)
    {
        _stateMachine = new StateMachine<TaskState>(Transitions, initialState);
        _stateMachine.StateChanged += OnStateChanged;
    }

    public TaskState CurrentState => _stateMachine.CurrentState;

    public event EventHandler<StateChangedEventArgs<TaskState>>? StateChanged;

    public bool TryTransitionTo(TaskState targetState) => _stateMachine.TryTransitionTo(targetState);

    public void ForceTransitionTo(TaskState targetState) => _stateMachine.ForceTransitionTo(targetState);

    public bool CanTransitionTo(TaskState targetState) => _stateMachine.CanTransitionTo(targetState);

    public IReadOnlySet<TaskState> GetValidNextStates() => _stateMachine.GetValidNextStates();

    public bool IsTerminalState()
    {
        return CurrentState is TaskState.Completed
               or TaskState.Failed
               or TaskState.Cancelled
               or TaskState.Stopped;
    }

    public bool CanExecute()
    {
        return CurrentState is TaskState.Pending or TaskState.WaitingForDependencies;
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
                TaskState.WaitingForDependencies,
                TaskState.InProgress,
                TaskState.Cancelled
            }.ToFrozenSet(),

            [TaskState.WaitingForDependencies] = new HashSet<TaskState>
            {
                TaskState.InProgress,
                TaskState.Cancelled
            }.ToFrozenSet(),

            [TaskState.InProgress] = new HashSet<TaskState>
            {
                TaskState.Paused,
                TaskState.Completed,
                TaskState.Failed,
                TaskState.Stopped
            }.ToFrozenSet(),

            [TaskState.Paused] = new HashSet<TaskState>
            {
                TaskState.InProgress,
                TaskState.Cancelled
            }.ToFrozenSet(),

            [TaskState.Completed] = new HashSet<TaskState>().ToFrozenSet(),
            [TaskState.Failed] = new HashSet<TaskState>().ToFrozenSet(),
            [TaskState.Cancelled] = new HashSet<TaskState>().ToFrozenSet(),
            [TaskState.Stopped] = new HashSet<TaskState>().ToFrozenSet()
        }.ToFrozenDictionary();
    }
}
