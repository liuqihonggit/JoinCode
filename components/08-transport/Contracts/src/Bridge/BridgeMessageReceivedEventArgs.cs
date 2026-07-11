namespace JoinCode.Transport.Bridge;

/// <summary>
/// Bridge 消息接收事件参数
/// </summary>
public class BridgeMessageReceivedEventArgs : EventArgs
{
    public BridgeMessage Message { get; }

    public BridgeMessageReceivedEventArgs(BridgeMessage message)
    {
        Message = message;
    }
}
