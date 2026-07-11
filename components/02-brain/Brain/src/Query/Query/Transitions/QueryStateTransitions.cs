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
public sealed class QueryStateTransitions : IQueryStateTransitions
{
    private static readonly FrozenDictionary<QueryState, FrozenSet<QueryState>> TransitionTable = CreateTransitionTable();

    private QueryState _currentState = QueryState.Idle;
    private readonly object _stateLock = new();
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;

    public QueryStateTransitions(ITelemetryService? telemetryService = null, IClockService? clock = null)
    {
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
    }

    public QueryState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
    }

    public event EventHandler<QueryStateChangedEventArgs>? StateChanged;

    public bool CanTransitionTo(QueryState from, QueryState to)
    {
        if (from == to)
        {
            return true;
        }

        return TransitionTable.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    public void TransitionTo(QueryState target)
    {
        QueryState oldState;
        QueryState newState;

        lock (_stateLock)
        {
            if (!CanTransitionTo(_currentState, target))
            {
                throw new InvalidOperationException(
                    $"Invalid state transition from {_currentState} to {target}");
            }

            oldState = _currentState;
            _currentState = target;
            newState = target;
        }

        StateChanged?.Invoke(this, new QueryStateChangedEventArgs
        {
            OldState = oldState,
            NewState = newState,
            Timestamp = _clock.GetUtcNow()
        });

        RecordTransitionMetrics(oldState, newState);
    }

    public void Reset()
    {
        QueryState oldState;

        lock (_stateLock)
        {
            oldState = _currentState;
            _currentState = QueryState.Idle;
        }

        if (oldState != QueryState.Idle)
        {
            StateChanged?.Invoke(this, new QueryStateChangedEventArgs
            {
                OldState = oldState,
                NewState = QueryState.Idle,
                Timestamp = _clock.GetUtcNow()
            });

            RecordTransitionMetrics(oldState, QueryState.Idle);
        }
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
