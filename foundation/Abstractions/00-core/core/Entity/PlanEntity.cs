namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 计划实体 — 派生自 Entity，与 Agent 同套路
/// 代表运行时计划（区别于 PlanState record，后者是数据模型 DTO）
/// </summary>
public sealed class PlanEntity : Entity
{
    public string? Description { get; init; }
    public PlanStatus Status { get; set; } = PlanStatus.Draft;
    public List<PlanStep> Steps { get; init; } = [];
    public int CurrentStepIndex { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public bool IsInPlanMode { get; set; } = true;
    public string? PlanFilePath { get; set; }
    public bool WasEditedByUser { get; set; }

    /// <summary>
    /// 全局唯一 Plan 注册器
    /// </summary>
    public static PlanEntityRegistry Registry { get; } = new();

    public PlanEntity(
        string? description = null,
        string? displayName = null)
        : base(ObjectType.Plan, displayName ?? description)
    {
        Description = description;
        LastUpdatedAt = DateTime.UtcNow;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose()
    {
        Registry.Remove(ObjectId);
    }

    public PlanState ToPlanState() => new()
    {
        PlanId = UniqueId,
        Description = Description,
        Status = Status,
        Steps = Steps,
        CurrentStepIndex = CurrentStepIndex,
        CreatedAt = CreatedAt,
        LastUpdatedAt = LastUpdatedAt,
        CompletedAt = CompletedAt,
        IsInPlanMode = IsInPlanMode,
        PlanFilePath = PlanFilePath,
        WasEditedByUser = WasEditedByUser
    };

    public int ApprovedStepsCount => Steps.Count(s => s.IsApproved);
    public int CompletedStepsCount => Steps.Count(s => s.IsCompleted);
    public int TotalSteps => Steps.Count;
}

/// <summary>
/// Plan 注册器 — 基于 MapRegistry
/// </summary>
public sealed class PlanEntityRegistry : MapRegistry<ObjectId, PlanEntity>
{
    internal void Add(ObjectId id, PlanEntity plan) => AddCore(id, plan);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    public IEnumerable<PlanEntity> GetByStatus(PlanStatus status) => Where(p => p.Status == status);
}
