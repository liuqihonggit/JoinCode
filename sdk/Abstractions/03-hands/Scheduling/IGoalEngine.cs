
namespace JoinCode.Abstractions.Interfaces.Scheduling;

/// <summary>
/// 目标引擎接口 — 模型可通过 MCP 工具查询和更新目标状态
/// </summary>
public interface IGoalEngine
{
    /// <summary>启动目标</summary>
    Task<GoalState> StartAsync(
        string objective,
        List<string>? constraints = null,
        int? tokenBudget = null,
        CancellationToken cancellationToken = default);

    /// <summary>暂停目标</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>恢复目标</summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>清除目标</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>标记目标为已完成（模型可调用，线程安全）</summary>
    Task MarkCompletedAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>标记目标为无法完成（模型可调用，线程安全）</summary>
    Task MarkUnmetAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>当前目标状态（无目标时为 null）</summary>
    GoalState? CurrentState { get; }

    /// <summary>是否有目标正在运行</summary>
    bool IsRunning { get; }
}
