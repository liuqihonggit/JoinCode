namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Goal 注册器 — 基于 MapRegistry，内部字典，对外暴露遍历器 + 字典视图
/// GetByStatus/GetPursuing 用次级索引 O(1) 查找（Status 可变，通过 TransitionStatus 同步索引）
/// </summary>
public sealed class GoalRegistry : MapRegistry<ObjectId, Goal> {
    private readonly SecondaryIndex<ObjectId, Goal, GoalStatus> _byStatus;

    /// <summary>构造 GoalRegistry，初始化次级索引</summary>
    public GoalRegistry() {
        _byStatus = CreateIndex(g => g.Status);
    }

    /// <summary>注册目标（internal，Goal构造时自动调用）</summary>
    internal void Add(ObjectId id, Goal goal) => AddCore(id, goal);

    /// <summary>注销目标（internal，Goal.Dispose时自动调用）</summary>
    internal bool Remove(ObjectId id) => RemoveCore(id);

    /// <summary>状态转换 — 更新 Goal.Status 并同步次级索引</summary>
    public void TransitionStatus(ObjectId id, GoalStatus newState) {
        var entity = Get(id);
        if (entity is null) return;
        var oldState = entity.Status;
        if (oldState == newState) return;
        entity.Status = newState;
        Reindex(_byStatus, id, oldState, newState);
    }

    /// <summary>按状态获取目标（O(1) 索引查找）</summary>
    public IEnumerable<Goal> GetByStatus(GoalStatus status)
        => _byStatus.GetValues(status, AsDictionary());

    /// <summary>获取正在追求的目标（O(1) 索引查找）</summary>
    public IEnumerable<Goal> GetPursuing()
        => _byStatus.GetValues(GoalStatus.Pursuing, AsDictionary());
}
