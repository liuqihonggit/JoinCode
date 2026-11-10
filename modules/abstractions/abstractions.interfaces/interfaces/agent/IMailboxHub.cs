namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 邮箱中枢接口 — 统一消息传递入口，按 MailboxKind 路由到多种通道
/// </summary>
public interface IMailboxHub {
    /// <summary>发送消息到指定 agent（自动路由 — 按 agent 注册的通道路由）</summary>
    ValueTask<bool> SendAsync(string agentId, CoordinatorMessage message, CancellationToken ct = default);

    /// <summary>广播消息（跨所有已注册通道）</summary>
    ValueTask BroadcastAsync(CoordinatorMessage message, CancellationToken ct = default);

    /// <summary>按可见性广播消息</summary>
    ValueTask BroadcastAsync(CoordinatorMessage message, MessageVisibility visibility, CancellationToken ct = default);

    /// <summary>从 agent 所在通道接收消息流</summary>
    IAsyncEnumerable<CoordinatorMessage> ReceiveAsync(string agentId, CancellationToken ct = default);

    /// <summary>注册 agent 到指定通道</summary>
    ValueTask RegisterAgentAsync(string agentId, string? sessionId = null, CancellationToken ct = default);

    /// <summary>注销 agent 邮箱</summary>
    ValueTask UnregisterAgentAsync(string agentId, CancellationToken ct = default);

    /// <summary>获取所有已注册 agent</summary>
    string[] GetRegisteredAgents();

    /// <summary>获取 agent 的会话 ID</summary>
    string? GetSessionId(string agentId);
}
