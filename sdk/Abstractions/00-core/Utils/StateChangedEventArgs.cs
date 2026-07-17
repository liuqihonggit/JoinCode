namespace JoinCode.Abstractions.Utils;

public sealed class StateChangedEventArgs<T> : EventArgs
{
    public T OldState { get; }
    public T NewState { get; }
    public DateTime Timestamp { get; }

    public StateChangedEventArgs(T oldState, T newState)
    {
        OldState = oldState;
        NewState = newState;
        Timestamp = DateTime.UtcNow;
    }
}
