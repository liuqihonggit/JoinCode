namespace JoinCode.Abstractions.State;

/// <summary>
/// Agent 状态枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 AgentStatusEnumConstants + AgentStatusExtensions
/// 合并自 JoinCode.Abstractions.Interfaces.AgentStatus (Pending/Stopped) 和 JoinCode.Abstractions.State.AgentStatus (Idle/Paused)
/// </summary>
public enum AgentStatus {
    /// <summary>等待启动</summary>
    [EnumValue("pending")] Pending = 0,

    /// <summary>空闲</summary>
    [EnumValue("idle")] Idle = 1,

    /// <summary>运行中</summary>
    [EnumValue("running")] Running = 2,

    /// <summary>已暂停</summary>
    [EnumValue("paused")] Paused = 3,

    /// <summary>已完成</summary>
    [EnumValue("completed")] Completed = 4,

    /// <summary>已失败</summary>
    [EnumValue("failed")] Failed = 5,

    /// <summary>已停止</summary>
    [EnumValue("stopped")] Stopped = 6
}

/// <summary>
/// Agent 状态扩展方法 — 提取自 AgentServiceImpl/SubAgent 等多处重复的终态/活跃态判断
/// </summary>
public static class AgentStatusHelper {
    /// <summary>
    /// 是否处于终态（Completed/Failed/Stopped）— 不可再转换
    /// </summary>
    public static bool IsTerminal(this AgentStatus status)
        => status is AgentStatus.Completed or AgentStatus.Failed or AgentStatus.Stopped;

    /// <summary>
    /// 是否处于活跃态（Running/Pending/Idle）— 正在执行或即将执行
    /// </summary>
    public static bool IsActive(this AgentStatus status)
        => status is AgentStatus.Running or AgentStatus.Pending or AgentStatus.Idle;
}
