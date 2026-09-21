namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 跨进程 Agent 注册信息 — 用于本机 agent 发现
/// </summary>
public sealed class AgentRegistryInfo {
    /// <summary>Agent 唯一标识</summary>
    public required string AgentId { get; init; }

    /// <summary>会话标识</summary>
    public required string SessionId { get; init; }

    /// <summary>操作系统进程 ID</summary>
    public required int ProcessId { get; init; }

    /// <summary>Agent 显示名称</summary>
    public string? DisplayName { get; init; }

    /// <summary>Agent 角色（如 Coordinator/Worker/Defender）</summary>
    public string? Role { get; init; }

    /// <summary>启动时间（UTC）</summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>最后心跳时间（UTC）— 用于活性检测</summary>
    public DateTimeOffset LastHeartbeat { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>进程是否存活（基于心跳判断）</summary>
    public bool IsAlive => DateTimeOffset.UtcNow - LastHeartbeat < TimeSpan.FromSeconds(30);

    /// <summary>投影为 AgentCoreIdentity（Role 从 string 转换为枚举）</summary>
    public AgentCoreIdentity ToIdentity() => new(AgentId, DisplayName, Role is not null && Enum.TryParse<AgentRole>(Role, out var role) ? role : null);
}