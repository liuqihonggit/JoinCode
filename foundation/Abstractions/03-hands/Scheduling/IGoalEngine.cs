
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
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>暂停目标</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>恢复目标</summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>清除目标</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 从持久化存储恢复活跃目标状态 — 进程重启后调用以恢复未完成的目标。
    /// 指定 goalId 时恢复该特定目标；未指定时恢复第一个活跃目标（单 goal 场景）。
    /// </summary>
    Task RehydrateAsync(CancellationToken cancellationToken = default, string? goalId = null);

    /// <summary>标记目标为已完成（模型可调用，线程安全）</summary>
    Task MarkCompletedAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>标记目标为无法完成（模型可调用，线程安全）</summary>
    Task MarkUnmetAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>当前目标状态（无目标时为 null）</summary>
    GoalState? CurrentState { get; }

    /// <summary>是否有目标正在运行</summary>
    bool IsRunning { get; }

    /// <summary>等待目标引擎循环退出（完成、预算耗尽、暂停、清除等）</summary>
    Task WaitForCompletionAsync(CancellationToken ct = default);

    /// <summary>设置 Graph 定义（由协调者 Agent 通过 MCP 工具调用）</summary>
    void SetGraphDefinition(string nodesJson, string edgesJson, string startNodeId, string endNodeIds);

    /// <summary>是否已有 Graph 定义</summary>
    bool HasGraphDefinition { get; }
}
