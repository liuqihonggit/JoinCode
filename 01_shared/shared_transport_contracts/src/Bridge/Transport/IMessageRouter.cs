namespace JoinCode.Transport.Bridge;

/// <summary>
/// 消息路由器接口 — 字符串级别的消息去重和分发
/// </summary>
public interface IMessageRouter : IAsyncDisposable
{
    /// <summary>接收到去重后的字符串消息</summary>
    event EventHandler<StringMessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// 处理接收到的字符串消息
    /// 通过 messageIdExtractor 从原始消息中提取 ID 进行去重
    /// </summary>
    Task ProcessMessageAsync(
        string messageJson,
        Func<string, string?> messageIdExtractor,
        CancellationToken cancellationToken = default);
}
