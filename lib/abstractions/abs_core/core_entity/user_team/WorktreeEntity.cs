namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Git Worktree 实体 — 派生自 Entity，追踪 git worktree 生命周期
/// 超时自动清理，避免遗忘的 worktree 占用磁盘
/// </summary>
public sealed class WorktreeEntity : Entity {
    /// <summary>获取 worktree 路径。</summary>
    public string WorktreePath { get; }
    /// <summary>获取或设置分支名称。</summary>
    public string? BranchName { get; init; }
    /// <summary>获取或设置关联代理标识。</summary>
    public ObjectId? AgentObjectId { get; init; }
    /// <summary>获取或设置 worktree 状态。</summary>
    public WorktreeEntityStatus Status { get; set; } = WorktreeEntityStatus.Active;

    /// <summary>获取全局唯一 Worktree 注册器。</summary>
    public static WorktreeEntityRegistry Registry { get; } = new();

    /// <summary>构造 worktree 实体。</summary>
    public WorktreeEntity(
        string worktreePath,
        string? branchName = null,
        ObjectId? agentObjectId = default,
        string? displayName = null,
        ObjectId sessionId = default)
        : base(ObjectType.Worktree, sessionId, displayName ?? worktreePath) {
        WorktreePath = worktreePath;
        BranchName = branchName;
        AgentObjectId = agentObjectId;
        Registry.Add(ObjectId, this);
    }

    public override void Dispose() {
        Registry.Remove(ObjectId);
        base.Dispose();
    }
}

/// <summary>
/// Worktree 状态
/// </summary>
public enum WorktreeEntityStatus {
    [EnumValue("active")] Active,
    [EnumValue("stale")] Stale,
    [EnumValue("removed")] Removed,
}

public sealed class WorktreeEntityRegistry : MapRegistry<ObjectId, WorktreeEntity> {
    internal void Add(ObjectId id, WorktreeEntity worktree) => AddCore(id, worktree);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    /// <summary>获取处于活跃状态的 worktree 实体列表。</summary>
    public IEnumerable<WorktreeEntity> GetActive() => Where(w => w.Status == WorktreeEntityStatus.Active);
    /// <summary>获取处于过期状态的 worktree 实体列表。</summary>
    public IEnumerable<WorktreeEntity> GetStale() => Where(w => w.Status == WorktreeEntityStatus.Stale);
}