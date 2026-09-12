namespace JoinCode.Abstractions.Interfaces.Scheduling;

/// <summary>
/// 自主循环执行器 — 轻量抽象，供需要"跑一个 agent 直到完成"的调用方使用（如 --doctor 模式）。
/// GoalEngine 是其主要实现。
/// 与 IGoalEngine 的区别：IGoalEngine 是完整目标引擎接口（含 pause/resume/clear/graph 等，供 /goal 命令使用）；
/// IAgentRunner 只暴露"启动 → 等待完成 → 查询状态"三件事，避免 doctor 等调用方寄生在 goal 引擎上。
/// </summary>
public interface IAgentRunner
{
    /// <summary>启动自主循环，给定目标与可选系统提示，返回初始状态</summary>
    Task<GoalState> RunAsync(
        string objective,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>等待循环退出（完成、预算耗尽、暂停、清除等）</summary>
    Task WaitForCompletionAsync(CancellationToken cancellationToken = default);

    /// <summary>当前状态（未启动时为 null）</summary>
    GoalState? CurrentState { get; }
}
