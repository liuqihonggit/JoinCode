namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 跨进程 Agent 发现接口 — 本机 agent 注册、心跳、发现
/// <para>每个 jcc.exe 进程启动时注册自己的 agent，其他进程可查询发现所有活跃 agent。</para>
/// <para>注册表持久化到 ~/.jcc/agents/registry.json，用 FileMailboxLock 保护写入。</para>
/// <para>心跳超时（默认30秒）的 agent 自动注销。</para>
/// </summary>
public interface IAgentDiscovery : IAsyncDisposable {
    /// <summary>注册本进程的 agent 到注册表</summary>
    /// <param name="info">Agent 注册信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task RegisterAsync(AgentRegistryInfo info, CancellationToken cancellationToken = default);

    /// <summary>注销指定 agent</summary>
    /// <param name="agentId">Agent 标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task UnregisterAsync(string agentId, string sessionId, CancellationToken cancellationToken = default);

    /// <summary>更新心跳时间 — 证明 agent 仍然活跃</summary>
    /// <param name="agentId">Agent 标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task HeartbeatAsync(string agentId, string sessionId, CancellationToken cancellationToken = default);

    /// <summary>发现所有活跃 agent（心跳未超时）</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>活跃 agent 列表</returns>
    Task<IReadOnlyList<AgentRegistryInfo>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>发现指定会话的所有活跃 agent</summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>活跃 agent 列表</returns>
    Task<IReadOnlyList<AgentRegistryInfo>> DiscoverBySessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>启动心跳循环 — 定期更新心跳时间</summary>
    /// <param name="agentId">Agent 标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="interval">心跳间隔（默认10秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务（循环直到取消）</returns>
    Task StartHeartbeatLoopAsync(string agentId, string sessionId, TimeSpan? interval = null, CancellationToken cancellationToken = default);
}