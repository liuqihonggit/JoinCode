
namespace JoinCode.Abstractions.Interfaces.Scheduling;

using JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 目标状态持久化存储 — 进程重启后可恢复目标状态。
/// 对齐文档 IGoalStateStore，复用 GoalState 模型，禁止创造新术语。
/// </summary>
public interface IGoalStateStore
{
    /// <summary>加载目标状态（不存在返回 null）</summary>
    Task<GoalState?> LoadAsync(string goalId, CancellationToken cancellationToken = default);

    /// <summary>保存目标状态（新增或更新，原子写入）</summary>
    Task SaveAsync(GoalState state, CancellationToken cancellationToken = default);

    /// <summary>删除目标状态</summary>
    Task DeleteAsync(string goalId, CancellationToken cancellationToken = default);

    /// <summary>获取所有未完成的目标（Status=Pursuing 或 Paused）</summary>
    Task<IReadOnlyList<GoalState>> GetActiveGoalsAsync(CancellationToken cancellationToken = default);
}
