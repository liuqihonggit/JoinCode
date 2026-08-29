
namespace JoinCode.Abstractions.Interfaces.Scheduling;

/// <summary>
/// 多目标注册表 — 管理多个 GoalEngine 实例，支持多 goal 并发和持久化恢复。
/// 对齐 PersistentDreamTaskRegistry 模式：内存缓存 + 持久化 + 启动恢复。
/// </summary>
public interface IGoalRegistry : IRegistry
{
    /// <summary>启动新目标（创建新 GoalEngine 实例）</summary>
    Task<GoalState> StartAsync(string objective, List<string>? constraints = null, int? tokenBudget = null, CancellationToken cancellationToken = default);

    /// <summary>获取当前活跃目标引擎</summary>
    IGoalEngine? CurrentEngine { get; }

    /// <summary>列出所有活跃目标状态</summary>
    Task<IReadOnlyList<GoalState>> ListActiveGoalsAsync(CancellationToken cancellationToken = default);

    /// <summary>获取指定目标引擎</summary>
    IGoalEngine? GetEngine(string goalId);

    /// <summary>切换当前活跃目标</summary>
    bool SetCurrent(string goalId);

    /// <summary>从持久化恢复所有活跃目标</summary>
    Task RehydrateAllAsync(CancellationToken cancellationToken = default);

    /// <summary>暂停当前目标</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>恢复当前目标</summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>清除当前目标</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>设置会话隔离标识 — 持久化按 {baseDir}/{sessionId}/{goalId}.json 隔离</summary>
    void SetSessionId(string sessionId);
}
