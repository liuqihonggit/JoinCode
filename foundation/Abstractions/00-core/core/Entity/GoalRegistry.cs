namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Goal 注册器 — 独立类，与 AgentRegistry 同套路
/// 管理所有 Goal 的注册/查询
/// 全局唯一实例通过 Goal.Registry 静态属性获取
/// </summary>
public sealed class GoalRegistry
{
    private readonly ConcurrentDictionary<ObjectId, Goal> _goals = new();

    /// <summary>
    /// 注册目标（internal，Goal构造时自动调用）
    /// </summary>
    internal void Add(ObjectId id, Goal goal)
    {
        _goals.TryAdd(id, goal);
    }

    /// <summary>
    /// 注销目标（internal，Goal.Dispose时自动调用）
    /// </summary>
    internal bool Remove(ObjectId id)
    {
        return _goals.TryRemove(id, out _);
    }

    /// <summary>
    /// 按ObjectId获取目标
    /// </summary>
    public Goal? Get(ObjectId id) => _goals.GetValueOrDefault(id);

    /// <summary>
    /// 获取所有目标
    /// </summary>
    public IReadOnlyList<Goal> GetAll() => [.. _goals.Values];

    /// <summary>
    /// 按状态获取目标
    /// </summary>
    public IReadOnlyList<Goal> GetByStatus(GoalStatus status)
        => [.. _goals.Values.Where(g => g.Status == status)];

    /// <summary>
    /// 获取正在追求的目标
    /// </summary>
    public IReadOnlyList<Goal> GetPursuing()
        => [.. _goals.Values.Where(g => g.Status == GoalStatus.Pursuing)];

    /// <summary>
    /// 当前注册的目标总数
    /// </summary>
    public int Count => _goals.Count;

    /// <summary>
    /// 清空所有注册（测试用）
    /// </summary>
    public void Clear()
    {
        _goals.Clear();
    }
}
