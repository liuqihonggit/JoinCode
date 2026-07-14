namespace Core.Utils;

public sealed class StateChangedEventArgs<TState> : EventArgs where TState : struct, Enum
{
    public required TState OldState { get; init; }
    public required TState NewState { get; init; }
    public DateTime Timestamp { get; init; }
}

public sealed class TransitionFailedEventArgs<TState> : EventArgs where TState : struct, Enum
{
    public required TState FromState { get; init; }
    public required TState ToState { get; init; }
    public DateTime Timestamp { get; init; }
}

public sealed class StateMachine<TState> where TState : struct, Enum
{
    private readonly FrozenDictionary<TState, FrozenSet<TState>> _transitions;
    private readonly FrozenSet<TState>? _terminalStates;
    private readonly object _lock = new();
    private readonly IClockService? _clock;
    private TState _currentState;

    public StateMachine(
        FrozenDictionary<TState, FrozenSet<TState>> transitions,
        TState initialState,
        IClockService? clock = null)
    {
        _transitions = transitions;
        _currentState = initialState;
        _clock = clock;
    }

    public StateMachine(
        FrozenDictionary<TState, FrozenSet<TState>> transitions,
        TState initialState,
        FrozenSet<TState> terminalStates,
        IClockService? clock = null)
    {
        _transitions = transitions;
        _currentState = initialState;
        _terminalStates = terminalStates;
        _clock = clock;
    }

    public TState CurrentState
    {
        get
        {
            lock (_lock)
            {
                return _currentState;
            }
        }
    }

    public event EventHandler<StateChangedEventArgs<TState>>? StateChanged;

    public bool CanTransitionTo(TState from, TState to)
    {
        if (from.Equals(to))
        {
            return true;
        }

        return _transitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    public bool CanTransitionTo(TState to)
    {
        lock (_lock)
        {
            return CanTransitionTo(_currentState, to);
        }
    }

    public void TransitionTo(TState target)
    {
        TState oldState;
        lock (_lock)
        {
            if (!CanTransitionTo(_currentState, target))
            {
                OnTransitionFailed(_currentState, target);
                throw new InvalidOperationException(
                    $"Invalid state transition from {_currentState} to {target}");
            }

            oldState = _currentState;
            _currentState = target;
        }

        OnStateChanged(oldState, target);
    }

    public bool TryTransitionTo(TState target)
    {
        TState oldState;
        bool changed;
        lock (_lock)
        {
            if (!CanTransitionTo(_currentState, target))
            {
                return false;
            }

            oldState = _currentState;
            _currentState = target;
            changed = true;
        }

        if (changed)
        {
            OnStateChanged(oldState, target);
        }

        return true;
    }

    public void ForceTransitionTo(TState target)
    {
        TState oldState;
        lock (_lock)
        {
            oldState = _currentState;
            _currentState = target;
        }

        OnStateChanged(oldState, target);
    }

    public void Reset(TState initialState)
    {
        TState oldState;
        lock (_lock)
        {
            oldState = _currentState;
            if (oldState.Equals(initialState))
            {
                return;
            }

            _currentState = initialState;
        }

        OnStateChanged(oldState, initialState);
    }

    public IReadOnlySet<TState> GetValidNextStates()
    {
        lock (_lock)
        {
            return _transitions.TryGetValue(_currentState, out var targets)
                ? targets
                : FrozenSet<TState>.Empty;
        }
    }

    public bool IsTerminalState()
    {
        if (_terminalStates is null)
        {
            lock (_lock)
            {
                return _transitions.TryGetValue(_currentState, out var targets) && targets.Count == 0;
            }
        }

        lock (_lock)
        {
            return _terminalStates.Contains(_currentState);
        }
    }

    public event EventHandler<TransitionFailedEventArgs<TState>>? TransitionFailed;

    private void OnStateChanged(TState oldState, TState newState)
    {
        var args = new StateChangedEventArgs<TState>
        {
            OldState = oldState,
            NewState = newState,
            Timestamp = _clock?.GetUtcNow() ?? DateTime.UtcNow
        };
        StateChanged?.Invoke(this, args);
    }

    private void OnTransitionFailed(TState fromState, TState toState)
    {
        var args = new TransitionFailedEventArgs<TState>
        {
            FromState = fromState,
            ToState = toState,
            Timestamp = _clock?.GetUtcNow() ?? DateTime.UtcNow
        };
        TransitionFailed?.Invoke(this, args);
    }
}
