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

public sealed class WorktreeEntityRegistry
{
    private readonly ConcurrentDictionary<ObjectId, WorktreeEntity> _worktrees = new();
    internal void Add(ObjectId id, WorktreeEntity worktree) => _worktrees.TryAdd(id, worktree);
    internal bool Remove(ObjectId id) => _worktrees.TryRemove(id, out _);
    public WorktreeEntity? Get(ObjectId id) => _worktrees.GetValueOrDefault(id);
    public IReadOnlyList<WorktreeEntity> GetAll() => [.. _worktrees.Values];
    public IReadOnlyList<WorktreeEntity> GetActive() => [.. _worktrees.Values.Where(w => w.Status == WorktreeEntityStatus.Active)];
    public IReadOnlyList<WorktreeEntity> GetStale() => [.. _worktrees.Values.Where(w => w.Status == WorktreeEntityStatus.Stale)];
    public int Count => _worktrees.Count;
    public void Clear() => _worktrees.Clear();
}
