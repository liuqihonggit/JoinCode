namespace JoinCode.Abstractions.Interfaces;

public interface INotificationService {
    /// <summary>异步发送通知。</summary>
    /// <param name="title">通知标题。</param>
    /// <param name="message">通知内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default);

    /// <summary>异步通知任务完成。</summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="description">任务描述。</param>
    /// <param name="success">是否成功。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task NotifyTaskCompletedAsync(string taskId, string description, bool success, CancellationToken cancellationToken = default);

    /// <summary>异步通知 Agent 消息。</summary>
    /// <param name="agentId">Agent 标识。</param>
    /// <param name="agentName">Agent 名称。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task NotifyAgentMessageAsync(string agentId, string agentName, string message, CancellationToken cancellationToken = default);

    /// <summary>获取通知服务是否可用。</summary>
    bool IsAvailable { get; }
}