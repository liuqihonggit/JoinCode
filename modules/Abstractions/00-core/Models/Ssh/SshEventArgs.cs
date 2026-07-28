
namespace JoinCode.Abstractions.Models.Ssh;

public sealed class SshSessionStateChangedEventArgs : EventArgs
{
    public required string SessionId { get; init; }
    public SshConnectionState NewState { get; init; }
    public SshConnectionState? PreviousState { get; init; }
    public Exception? Error { get; init; }
}

public sealed class SshConnectionStateChangedEventArgs : EventArgs
{
    public required string SessionId { get; init; }
    public SshConnectionState NewState { get; init; }
    public SshConnectionState PreviousState { get; init; }
    public Exception? Error { get; init; }
}
