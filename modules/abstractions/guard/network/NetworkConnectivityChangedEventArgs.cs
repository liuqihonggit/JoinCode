namespace JoinCode.Abstractions.Network;

/// <summary>
/// 网络连接状态变化事件参数
/// </summary>
public sealed partial class NetworkConnectivityChangedEventArgs : EventArgs {
    /// <summary>获取先前连接状态。</summary>
    public NetworkConnectivityState PreviousState { get; init; }
    /// <summary>获取当前连接状态。</summary>
    public NetworkConnectivityState CurrentState { get; init; }
    /// <summary>获取状态变化时间戳。</summary>
    public DateTimeOffset Timestamp { get; init; }
    /// <summary>获取变化原因。</summary>
    public string? Reason { get; init; }
}
