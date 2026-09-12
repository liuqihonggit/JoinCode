namespace JoinCode.Abstractions.Network;

/// <summary>
/// 网络连接状态变化事件参数
/// </summary>
public sealed partial class NetworkConnectivityChangedEventArgs : EventArgs
{
    public NetworkConnectivityState PreviousState { get; init; }
    public NetworkConnectivityState CurrentState { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string? Reason { get; init; }
}
