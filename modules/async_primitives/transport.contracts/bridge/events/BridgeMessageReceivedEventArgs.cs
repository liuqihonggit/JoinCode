namespace JoinCode.Transport.Bridge;

/// <summary>
/// Bridge 消息接收事件参数
/// </summary>
public class BridgeMessageReceivedEventArgs : EventArgs {
    /// <summary>接收到的 Bridge 消息</summary>
    public BridgeMessage Message { get; }

    /// <summary>
    /// 初始化 Bridge 消息接收事件参数
    /// </summary>
    /// <param name="message">接收到的 Bridge 消息</param>
    public BridgeMessageReceivedEventArgs(BridgeMessage message) {
        Message = message;
    }
}