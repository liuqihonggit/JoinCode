
namespace JoinCode.Abstractions.Interfaces.Scheduling;

/// <summary>
/// 目标状态持久化存储 — 按 sessionId 隔离，进程重启后可恢复目标状态。
/// 路径模式: {baseDir}/{sessionId}/{goalId}.json
/// 对齐文档 IGoalStateStore，复用 GoalState 模型，禁止创造新术语。
/// </summary>
public interface IGoalStateStore : IStore
{
    /// <summary>加载目标状态（不存在返回 null）</summary>
    Task<GoalState?> LoadAsync(string sessionId, string goalId, CancellationToken cancellationToken = default);

    /// <summary>保存目标状态（新增或更新，原子写入）。state.SessionId 确定隔离目录。</summary>
    Task SaveAsync(GoalState state, CancellationToken cancellationToken = default);

    /// <summary>删除目标状态</summary>
    Task DeleteAsync(string sessionId, string goalId, CancellationToken cancellationToken = default);

    /// <summary>获取指定会话的所有未完成目标（Status=Pursuing 或 Paused）</summary>
    Task<IReadOnlyList<GoalState>> GetActiveGoalsAsync(string sessionId, CancellationToken cancellationToken = default);
}
