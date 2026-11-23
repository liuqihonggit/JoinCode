namespace JoinCode.Abstractions.Utils;

public sealed class StateChangedEventArgs<T> : EventArgs {
    /// <summary>获取旧状态。</summary>
    public T OldState { get; }
    /// <summary>获取新状态。</summary>
    public T NewState { get; }
    /// <summary>获取时间戳。</summary>
    public DateTime Timestamp { get; }

    /// <summary>构造状态变更事件参数。</summary>
    /// <param name="oldState">旧状态。</param>
    /// <param name="newState">新状态。</param>
    public StateChangedEventArgs(T oldState, T newState) {
        OldState = oldState;
        NewState = newState;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>构造状态变更事件参数。</summary>
    /// <param name="oldState">旧状态。</param>
    /// <param name="newState">新状态。</param>
    /// <param name="timestamp">时间戳。</param>
    public StateChangedEventArgs(T oldState, T newState, DateTime timestamp) {
        OldState = oldState;
        NewState = newState;
        Timestamp = timestamp;
    }
}