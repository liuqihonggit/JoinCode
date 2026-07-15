namespace Core.Query.Transitions;

public enum QueryState
{
    [EnumValue("idle")] Idle,
    [EnumValue("initializing")] Initializing,
    [EnumValue("running")] Running,
    [EnumValue("waitingForTool")] WaitingForTool,
    [EnumValue("executingTool")] ExecutingTool,
    [EnumValue("compacting")] Compacting,
    [EnumValue("stopping")] Stopping,
    [EnumValue("completed")] Completed,
    [EnumValue("failed")] Failed,
    [EnumValue("cancelled")] Cancelled
}

public sealed class QueryStateChangedEventArgs : EventArgs
{
    public required QueryState OldState { get; init; }
    public required QueryState NewState { get; init; }
    public required DateTime Timestamp { get; init; }
}

public interface IQueryStateTransitions
{
    QueryState CurrentState { get; }
    bool CanTransitionTo(QueryState from, QueryState to);
    void TransitionTo(QueryState target);
    void Reset();
    event EventHandler<QueryStateChangedEventArgs>? StateChanged;
}

[Register(typeof(IQueryStateTransitions))]
public sealed partial class QueryStateTransitions : IQueryStateTransitions
{
    private static readonly FrozenDictionary<QueryState, FrozenSet<QueryState>> TransitionTable = CreateTransitionTable();

    private readonly StateMachine<QueryState> _stateMachine;
    private readonly ITelemetryService? _telemetryService;

    public QueryStateTransitions(ITelemetryService? telemetryService = null, IClockService? clock = null)
    {
        _telemetryService = telemetryService;
        _stateMachine = new StateMachine<QueryState>(TransitionTable, QueryState.Idle, clock);
        _stateMachine.StateChanged += OnStateChanged;
    }

    public QueryState CurrentState => _stateMachine.CurrentState;

    public event EventHandler<QueryStateChangedEventArgs>? StateChanged;

    public bool CanTransitionTo(QueryState from, QueryState to) => _stateMachine.CanTransitionTo(from, to);

    public void TransitionTo(QueryState target) => _stateMachine.TransitionTo(target);

    public void Reset() => _stateMachine.Reset(QueryState.Idle);

    private void OnStateChanged(object? sender, StateChangedEventArgs<QueryState> e)
    {
        StateChanged?.Invoke(this, new QueryStateChangedEventArgs
        {
            OldState = e.OldState,
            NewState = e.NewState,
            Timestamp = e.Timestamp
        });

        RecordTransitionMetrics(e.OldState, e.NewState);
    }

    private void RecordTransitionMetrics(QueryState from, QueryState to)
        => _telemetryService?.RecordCount("query.state.transition.count", new() { ["from"] = from.ToString(), ["to"] = to.ToString() }, "count", "Query state transition count");

    private static FrozenDictionary<QueryState, FrozenSet<QueryState>> CreateTransitionTable()
    {
        var builder = new Dictionary<QueryState, FrozenSet<QueryState>>
        {
            [QueryState.Idle] = new HashSet<QueryState> { QueryState.Initializing }.ToFrozenSet(),
            [QueryState.Initializing] = new HashSet<QueryState> { QueryState.Running, QueryState.Failed, QueryState.Cancelled }.ToFrozenSet(),
            [QueryState.Running] = new HashSet<QueryState> { QueryState.WaitingForTool, QueryState.Compacting, QueryState.Stopping, QueryState.Completed, QueryState.Failed, QueryState.Cancelled }.ToFrozenSet(),
            [QueryState.WaitingForTool] = new HashSet<QueryState> { QueryState.ExecutingTool, QueryState.Stopping, QueryState.Failed, QueryState.Cancelled }.ToFrozenSet(),
            [QueryState.ExecutingTool] = new HashSet<QueryState> { QueryState.Running, QueryState.Stopping, QueryState.Failed, QueryState.Cancelled }.ToFrozenSet(),
            [QueryState.Compacting] = new HashSet<QueryState> { QueryState.Running, QueryState.Stopping, QueryState.Failed, QueryState.Cancelled }.ToFrozenSet(),
            [QueryState.Stopping] = new HashSet<QueryState> { QueryState.Completed, QueryState.Failed, QueryState.Cancelled }.ToFrozenSet(),
            [QueryState.Completed] = new HashSet<QueryState> { QueryState.Idle }.ToFrozenSet(),
            [QueryState.Failed] = new HashSet<QueryState> { QueryState.Idle }.ToFrozenSet(),
            [QueryState.Cancelled] = new HashSet<QueryState> { QueryState.Idle }.ToFrozenSet()
        };

        return builder.ToFrozenDictionary();
    }
}
