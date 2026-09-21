namespace JoinCode.Abstractions.Utils;

public sealed class TransitionFailedEventArgs<TState> : EventArgs where TState : struct, Enum {
    /// <summary>获取源状态。</summary>
    public required TState FromState { get; init; }
    /// <summary>获取目标状态。</summary>
    public required TState ToState { get; init; }
    /// <summary>获取转换失败时间戳。</summary>
    public DateTime Timestamp { get; init; }
}

public sealed class StateMachine<TState> where TState : struct, Enum {
    private readonly int[] _transitionMasks;
    private readonly FrozenSet<TState>?[] _validNextStates;
    private readonly FrozenSet<TState> _terminalStates = FrozenSet<TState>.Empty;
    private readonly AsyncLock _lock = new("StateMachine");
    private readonly IClockService? _clock;
    private TState _currentState;

    /// <summary>构造状态机。</summary>
    /// <param name="transitions">状态转移表。</param>
    /// <param name="initialState">初始状态。</param>
    /// <param name="clock">时钟服务。</param>
    public StateMachine(
        FrozenDictionary<TState, FrozenSet<TState>> transitions,
        TState initialState,
        IClockService? clock = null) {
        _transitionMasks = BuildTransitionMasks(transitions);
        _validNextStates = BuildValidNextStates(transitions);
        _currentState = initialState;
        _clock = clock;
    }

    /// <summary>构造状态机（含终态集合）。</summary>
    /// <param name="transitions">状态转移表。</param>
    /// <param name="initialState">初始状态。</param>
    /// <param name="terminalStates">终态集合。</param>
    /// <param name="clock">时钟服务。</param>
    public StateMachine(
        FrozenDictionary<TState, FrozenSet<TState>> transitions,
        TState initialState,
        FrozenSet<TState> terminalStates,
        IClockService? clock = null) {
        _transitionMasks = BuildTransitionMasks(transitions);
        _validNextStates = BuildValidNextStates(transitions);
        _currentState = initialState;
        _terminalStates = terminalStates;
        _clock = clock;
    }

    /// <summary>
    /// 从 FrozenDictionary 转移表构建 int[] 位掩码数组 — 用于 CanTransitionTo 热路径 O(1) 位运算。
    /// 索引为 (int)TState，值为目标状态位掩码。AOT 友好，构造时一次性分配。
    /// </summary>
    private static int[] BuildTransitionMasks(FrozenDictionary<TState, FrozenSet<TState>> transitions) {
        var masks = new int[Enum.GetValues<TState>().Length];
        foreach (var kvp in transitions) {
            var key = kvp.Key;
            var mask = 0;
            foreach (var t in kvp.Value) {
                var tv = t;
                mask |= 1 << Unsafe.As<TState, int>(ref tv);
            }
            masks[Unsafe.As<TState, int>(ref key)] = mask;
        }
        return masks;
    }

    /// <summary>
    /// 从 FrozenDictionary 转移表构建 FrozenSet&lt;TState&gt;?[] 数组 — 用于 GetValidNextStates/IsTerminalState 冷路径。
    /// 索引为 (int)TState，值为该状态的合法目标集合，null 表示状态不在转移表。AOT 友好，构造时一次性分配。
    /// </summary>
    private static FrozenSet<TState>?[] BuildValidNextStates(FrozenDictionary<TState, FrozenSet<TState>> transitions) {
        var validNext = new FrozenSet<TState>?[Enum.GetValues<TState>().Length];
        foreach (var kvp in transitions) {
            var key = kvp.Key;
            validNext[Unsafe.As<TState, int>(ref key)] = kvp.Value;
        }
        return validNext;
    }

    /// <summary>获取当前状态。</summary>
    public TState CurrentState {
        get {
            using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
                return _currentState;
            }
        }
    }

    public event EventHandler<StateChangedEventArgs<TState>>? StateChanged;

    /// <summary>判断从指定状态是否能转移到目标状态。</summary>
    /// <param name="from">源状态。</param>
    /// <param name="to">目标状态。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanTransitionTo(TState from, TState to) {
        if (from.Equals(to)) {
            return true;
        }

        var fromIdx = Unsafe.As<TState, int>(ref from);
        return fromIdx < _transitionMasks.Length && BitMask.Contains(_transitionMasks[fromIdx], to);
    }

    /// <summary>判断从当前状态是否能转移到目标状态。</summary>
    /// <param name="to">目标状态。</param>
    public bool CanTransitionTo(TState to) {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            return CanTransitionTo(_currentState, to);
        }
    }

    /// <summary>转移到目标状态，非法转移抛出异常。</summary>
    /// <param name="target">目标状态。</param>
    public void TransitionTo(TState target) {
        TState oldState;
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            if (!CanTransitionTo(_currentState, target)) {
                OnTransitionFailed(_currentState, target);
                throw new InvalidOperationException(
                    $"Invalid state transition from {_currentState} to {target}");
            }

            oldState = _currentState;
            _currentState = target;
        }

        OnStateChanged(oldState, target);
    }

    /// <summary>尝试转移到目标状态，失败返回 false。</summary>
    /// <param name="target">目标状态。</param>
    public bool TryTransitionTo(TState target) {
        TState oldState;
        bool changed;
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            if (!CanTransitionTo(_currentState, target)) {
                return false;
            }

            oldState = _currentState;
            _currentState = target;
            changed = true;
        }

        if (changed) {
            OnStateChanged(oldState, target);
        }

        return true;
    }

    /// <summary>强制转移到目标状态，不校验合法性。</summary>
    /// <param name="target">目标状态。</param>
    public void ForceTransitionTo(TState target) {
        TState oldState;
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            oldState = _currentState;
            _currentState = target;
        }

        OnStateChanged(oldState, target);
    }

    /// <summary>重置到指定初始状态。</summary>
    /// <param name="initialState">初始状态。</param>
    public void Reset(TState initialState) {
        TState oldState;
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            oldState = _currentState;
            if (oldState.Equals(initialState)) {
                return;
            }

            _currentState = initialState;
        }

        OnStateChanged(oldState, initialState);
    }

    /// <summary>获取当前状态的合法下一状态集合。</summary>
    public IReadOnlySet<TState> GetValidNextStates() {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            var currentIdx = Unsafe.As<TState, int>(ref _currentState);
            return currentIdx < _validNextStates.Length && _validNextStates[currentIdx] is { } targets
                ? targets
                : FrozenSet<TState>.Empty;
        }
    }

    /// <summary>判断当前状态是否为终态。</summary>
    public bool IsTerminalState() {
        if (_terminalStates.Count == 0) {
            using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
                var currentIdx = Unsafe.As<TState, int>(ref _currentState);
                return currentIdx < _validNextStates.Length && _validNextStates[currentIdx] is { Count: 0 };
            }
        }

        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            return _terminalStates.Contains(_currentState);
        }
    }

    public event EventHandler<TransitionFailedEventArgs<TState>>? TransitionFailed;

    private void OnStateChanged(TState oldState, TState newState) {
        var timestamp = _clock?.GetUtcNow() ?? DateTime.UtcNow;
        var args = new StateChangedEventArgs<TState>(oldState, newState, timestamp);
        StateChanged?.Invoke(this, args);
    }

    private void OnTransitionFailed(TState fromState, TState toState) {
        var args = new TransitionFailedEventArgs<TState> {
            FromState = fromState,
            ToState = toState,
            Timestamp = _clock?.GetUtcNow() ?? DateTime.UtcNow
        };
        TransitionFailed?.Invoke(this, args);
    }
}
