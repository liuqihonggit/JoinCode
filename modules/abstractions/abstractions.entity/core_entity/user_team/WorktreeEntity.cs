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
    private readonly SecondaryIndex<ObjectId, WorktreeEntity, WorktreeEntityStatus> _byStatus;

    /// <summary>构造 WorktreeEntityRegistry，初始化次级索引</summary>
    public WorktreeEntityRegistry() {
        _byStatus = CreateIndex(w => w.Status);
    }

    internal void Add(ObjectId id, WorktreeEntity worktree) => AddCore(id, worktree);
    internal bool Remove(ObjectId id) => RemoveCore(id);

    /// <summary>状态转换 — 更新 WorktreeEntity.Status 并同步次级索引</summary>
    public void TransitionStatus(ObjectId id, WorktreeEntityStatus newState) {
        var entity = Get(id);
        if (entity is null) return;
        var oldState = entity.Status;
        if (oldState == newState) return;
        entity.Status = newState;
        Reindex(_byStatus, id, oldState, newState);
    }

    /// <summary>获取处于活跃状态的 worktree 实体列表（O(1) 索引查找）。</summary>
    public IEnumerable<WorktreeEntity> GetActive() => _byStatus.GetValues(WorktreeEntityStatus.Active, AsDictionary());
    /// <summary>获取处于过期状态的 worktree 实体列表（O(1) 索引查找）。</summary>
    public IEnumerable<WorktreeEntity> GetStale() => _byStatus.GetValues(WorktreeEntityStatus.Stale, AsDictionary());
}