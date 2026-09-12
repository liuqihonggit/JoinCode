namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Goal 注册器 — 基于 MapRegistry，内部字典，对外暴露遍历器 + 字典视图
/// </summary>
public sealed class GoalRegistry : MapRegistry<ObjectId, Goal>
{
    /// <summary>注册目标（internal，Goal构造时自动调用）</summary>
    internal void Add(ObjectId id, Goal goal) => AddCore(id, goal);

    /// <summary>注销目标（internal，Goal.Dispose时自动调用）</summary>
    internal bool Remove(ObjectId id) => RemoveCore(id);

    /// <summary>按状态获取目标</summary>
    public IEnumerable<Goal> GetByStatus(GoalStatus status)
        => Where(g => g.Status == status);

    /// <summary>获取正在追求的目标</summary>
    public IEnumerable<Goal> GetPursuing()
        => Where(g => g.Status == GoalStatus.Pursuing);
}
