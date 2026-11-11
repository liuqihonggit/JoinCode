
namespace JoinCode.Abstractions.Models.Ssh;

public sealed class SshSessionStateChangedEventArgs : EventArgs {
    /// <summary>获取会话标识。</summary>
    public required string SessionId { get; init; }
    /// <summary>获取新状态。</summary>
    public SshConnectionState NewState { get; init; }
    /// <summary>获取前一状态。</summary>
    public SshConnectionState? PreviousState { get; init; }
    /// <summary>获取异常信息。</summary>
    public Exception? Error { get; init; }
}

public sealed class SshConnectionStateChangedEventArgs : EventArgs {
    /// <summary>获取会话标识。</summary>
    public required string SessionId { get; init; }
    /// <summary>获取新状态。</summary>
    public SshConnectionState NewState { get; init; }
    /// <summary>获取前一状态。</summary>
    public SshConnectionState PreviousState { get; init; }
    /// <summary>获取异常信息。</summary>
    public Exception? Error { get; init; }
}