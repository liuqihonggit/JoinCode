
namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// Agent 记忆作用域 — 对齐 TS agentMemory.ts 的 AgentMemoryScope
/// user: 用户全局级，跨所有项目共享
/// project: 项目级，通过版本控制共享，入 VCS
/// local: 本地级，不入版本控制，针对本项目和本机
/// </summary>
public enum AgentMemoryScope {
    [EnumValue("user")] User,
    [EnumValue("project")] Project,
    [EnumValue("local")] Local,
}

/// <summary>
/// Agent 记忆快照操作类型
/// </summary>
public enum AgentMemorySnapshotAction {
    /// <summary>无需操作</summary>
    [EnumValue("none")]
    None,
    /// <summary>首次从快照初始化</summary>
    [EnumValue("initialize")]
    Initialize,
    /// <summary>需要提示更新</summary>
    [EnumValue("promptUpdate")]
    PromptUpdate,
}

/// <summary>
/// Agent 记忆快照检查结果
/// </summary>
public sealed record AgentMemorySnapshotCheck {
    /// <summary>获取快照操作类型。</summary>
    public required AgentMemorySnapshotAction Action { get; init; }
    /// <summary>获取快照时间戳。</summary>
    public string? SnapshotTimestamp { get; init; }
}