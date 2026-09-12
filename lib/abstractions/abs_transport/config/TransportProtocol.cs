namespace JoinCode.Abstractions.Transport;

/// <summary>
/// 传输协议类型
/// </summary>
public enum TransportProtocol
{
    [EnumValue("websocket")] WebSocket,
    [EnumValue("sse")] Sse
}

/// <summary>
/// 传输连接状态
/// </summary>
public enum TransportConnectionState
{
    [EnumValue("disconnected")] Disconnected,
    [EnumValue("connecting")] Connecting,
    [EnumValue("connected")] Connected,
    [EnumValue("reconnecting")] Reconnecting,
    [EnumValue("error")] Error
}
