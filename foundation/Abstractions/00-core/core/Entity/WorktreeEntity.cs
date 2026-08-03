namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Git Worktree 实体 — 派生自 Entity，追踪 git worktree 生命周期
/// 超时自动清理，避免遗忘的 worktree 占用磁盘
/// </summary>
public sealed class WorktreeEntity : Entity
{
    public string WorktreePath { get; }
    public string? BranchName { get; init; }
    public ObjectId? AgentObjectId { get; init; }
    public WorktreeEntityStatus Status { get; set; } = WorktreeEntityStatus.Active;

    public static WorktreeEntityRegistry Registry { get; } = new();

    public WorktreeEntity(
        string worktreePath,
        string? branchName = null,
        ObjectId? agentObjectId = default,
        string? displayName = null)
        : base(ObjectType.Worktree, displayName ?? worktreePath)
    {
        WorktreePath = worktreePath;
        BranchName = branchName;
        AgentObjectId = agentObjectId;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose()
    {
        Registry.Remove(ObjectId);
    }
}

/// <summary>
/// Worktree 状态
/// </summary>
public enum WorktreeEntityStatus
{
    [EnumValue("active")] Active,
    [EnumValue("stale")] Stale,
    [EnumValue("removed")] Removed,
}

public sealed class WorktreeEntityRegistry : MapRegistry<ObjectId, WorktreeEntity>
{
    internal void Add(ObjectId id, WorktreeEntity worktree) => AddCore(id, worktree);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    public IEnumerable<WorktreeEntity> GetActive() => Where(w => w.Status == WorktreeEntityStatus.Active);
    public IEnumerable<WorktreeEntity> GetStale() => Where(w => w.Status == WorktreeEntityStatus.Stale);
}
