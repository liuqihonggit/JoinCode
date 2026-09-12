namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 邮箱消息接收槽 — MailboxPoller 投递跨进程消息的目标。
/// 实现方（如 CommandQueue 适配器）接收消息后入队等待处理，断开 Broker→Poller→Broker 循环。
/// </summary>
public interface IMailboxMessageSink
{
    /// <summary>投递消息到接收槽。</summary>
    /// <param name="agentId">目标 agent ID。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DeliverAsync(string agentId, CoordinatorMessage message, CancellationToken cancellationToken = default);
}
