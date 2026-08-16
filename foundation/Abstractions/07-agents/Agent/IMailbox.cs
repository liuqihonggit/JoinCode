namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 统一邮箱接口 — 所有 agent 间消息传递均通过邮箱，废弃 Channel 直传命名。
/// 同进程用 InProcessMailbox（内存邮箱），跨进程用 TeammateMailboxService（文件邮箱）。
/// 对齐 claude code 的 teammateMailbox + mailbox 双模式设计。
/// </summary>
public interface IMailbox
{
    /// <summary>注册 agent 邮箱。</summary>
    void RegisterAgent(string agentId, string? sessionId = null);

    /// <summary>注销 agent 邮箱。</summary>
    void UnregisterAgent(string agentId);

    /// <summary>发送消息到指定 agent 邮箱。</summary>
    Task<bool> SendAsync(string agentId, CoordinatorMessage message, CancellationToken cancellationToken = default);

    /// <summary>广播消息到所有已注册邮箱（排除发送者）。</summary>
    Task BroadcastAsync(CoordinatorMessage message, CancellationToken cancellationToken = default);

    /// <summary>从指定 agent 邮箱接收消息流。</summary>
    IAsyncEnumerable<CoordinatorMessage> ReceiveAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>获取所有已注册邮箱的 agent。</summary>
    IReadOnlyCollection<string> GetRegisteredAgents();

    /// <summary>获取 agent 的会话 ID。</summary>
    string? GetSessionId(string agentId);
}
