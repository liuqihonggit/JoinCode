namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Agent 输出 chunk — 子代理/主代理的流式输出单元
/// </summary>
public readonly record struct AgentOutputChunk
{
    /// <summary>产生此 chunk 的 Agent ID</summary>
    public required string AgentId { get; init; }

    /// <summary>Agent 显示名（可为 null）</summary>
    public string? AgentName { get; init; }

    /// <summary>输出内容</summary>
    public required string Content { get; init; }

    /// <summary>chunk 类型</summary>
    public AgentOutputChunkType Type { get; init; }
}

/// <summary>
/// Agent 输出 chunk 类型
/// </summary>
public enum AgentOutputChunkType
{
    /// <summary>正文输出</summary>
    Text,

    /// <summary>思考过程</summary>
    Thinking,

    /// <summary>执行完成</summary>
    Complete,

    /// <summary>错误信息</summary>
    Error
}

/// <summary>
/// 活跃 Agent 输出信息
/// </summary>
public sealed record AgentOutputInfo
{
    public required string AgentId { get; init; }
    public string? DisplayName { get; init; }
}

/// <summary>
/// Agent 输出 channel 管理器 — 汇聚所有 Agent 的流式输出到一个 channel
/// 前台通过 ReadAllAsync 拉取显示，/switch 命令通过 AgentOutputDisplayMode 过滤
/// </summary>
public interface IAgentOutputChannelManager
{
    /// <summary>
    /// 注册 Agent（启动时调用）
    /// </summary>
    void Register(string agentId, string? displayName);

    /// <summary>
    /// 注销 Agent（结束时调用）
    /// </summary>
    void Unregister(string agentId);

    /// <summary>
    /// 向输出 channel 写入 chunk
    /// </summary>
    void Write(string agentId, string? agentName, string content, AgentOutputChunkType type);

    /// <summary>
    /// 从输出 channel 拉取所有 chunk（前台显示用，阻塞式 IAsyncEnumerable）
    /// </summary>
    IAsyncEnumerable<AgentOutputChunk> ReadAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 获取所有活跃 Agent 列表
    /// </summary>
    IReadOnlyList<AgentOutputInfo> GetActiveAgents();
}
