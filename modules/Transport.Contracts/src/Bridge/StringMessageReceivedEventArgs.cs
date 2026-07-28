namespace JoinCode.Transport.Bridge;

/// <summary>
/// 字符串消息接收事件参数
/// </summary>
public sealed class StringMessageReceivedEventArgs : EventArgs
{
    /// <summary>
    /// 原始消息 JSON 字符串
    /// </summary>
    public string MessageJson { get; }

    /// <summary>
    /// 消息 ID（用于去重）
    /// </summary>
    public string MessageId { get; }

    public StringMessageReceivedEventArgs(string messageJson, string messageId)
    {
        MessageJson = messageJson;
        MessageId = messageId;
    }
}
