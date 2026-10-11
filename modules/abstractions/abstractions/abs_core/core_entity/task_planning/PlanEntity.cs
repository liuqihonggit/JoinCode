namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 计划实体 — 派生自 Entity，与 Agent 同套路
/// 代表运行时计划（区别于 PlanState record，后者是数据模型 DTO）
/// </summary>
public sealed class PlanEntity : Entity {
    /// <summary>获取或设置计划描述。</summary>
    public string? Description { get; init; }
    /// <summary>获取或设置计划状态。</summary>
    public PlanStatus Status { get; set; } = PlanStatus.Draft;
    /// <summary>获取计划步骤列表。</summary>
    public List<PlanStep> Steps { get; init; } = [];
    /// <summary>获取或设置当前步骤索引。</summary>
    public int CurrentStepIndex { get; set; }
    /// <summary>获取或设置最近更新时间。</summary>
    public DateTime LastUpdatedAt { get; set; }
    /// <summary>获取或设置是否处于计划模式。</summary>
    public bool IsInPlanMode { get; set; } = true;
    /// <summary>获取或设置计划文件路径。</summary>
    public string? PlanFilePath { get; set; }
    /// <summary>获取或设置是否被用户编辑过。</summary>
    public bool WasEditedByUser { get; set; }

    /// <summary>
    /// 全局唯一 Plan 注册器
    /// </summary>
    public static PlanEntityRegistry Registry { get; } = new();

    /// <summary>构造 PlanEntity 实例并注册到全局注册器。</summary>
    public PlanEntity(
        string? description = null,
        string? displayName = null,
        ObjectId sessionId = default)
        : base(ObjectType.Plan, sessionId, displayName ?? description) {
        Description = description;
        LastUpdatedAt = DateTime.UtcNow;
        Registry.Add(ObjectId, this);
    }

    /// <summary>释放资源。</summary>
    public override void Dispose() {
        Registry.Remove(ObjectId);
        base.Dispose();
    }

    /// <summary>转换为 PlanState 数据模型。</summary>
    public PlanState ToPlanState() => new() {
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

    /// <summary>获取已批准步骤数。</summary>
    public int ApprovedStepsCount => Steps.Count(s => s.IsApproved);
    /// <summary>获取已完成步骤数。</summary>
    public int CompletedStepsCount => Steps.Count(s => s.IsCompleted);
    /// <summary>获取总步骤数。</summary>
    public int TotalSteps => Steps.Count;

    /// <summary>
    /// 跨会话深拷贝 — 新 ObjectId + 目标会话，深拷贝所有字段
    /// </summary>
    public override Entity Clone(CloneContext context) {
        var cloned = new PlanEntity(
            description: Description,
            displayName: DisplayName,
            sessionId: context.TargetSessionId) {
            Status = Status,
            Steps = new List<PlanStep>(Steps),
            CurrentStepIndex = CurrentStepIndex,
            LastUpdatedAt = LastUpdatedAt,
            IsInPlanMode = IsInPlanMode,
            PlanFilePath = PlanFilePath,
            WasEditedByUser = WasEditedByUser
        };
        context.Map(ObjectId, cloned.ObjectId);
        return cloned;
    }
}

/// <summary>
/// Plan 注册器 — 基于 MapRegistry
/// </summary>
public sealed class PlanEntityRegistry : MapRegistry<ObjectId, PlanEntity> {
    private readonly SecondaryIndex<ObjectId, PlanEntity, PlanStatus> _byStatus;

    /// <summary>构造 PlanEntityRegistry，初始化次级索引</summary>
    public PlanEntityRegistry() {
        _byStatus = CreateIndex(p => p.Status);
    }

    internal void Add(ObjectId id, PlanEntity plan) => AddCore(id, plan);
    internal bool Remove(ObjectId id) => RemoveCore(id);

    /// <summary>状态转换 — 更新 PlanEntity.Status 并同步次级索引</summary>
    public void TransitionStatus(ObjectId id, PlanStatus newState) {
        var entity = Get(id);
        if (entity is null) return;
        var oldState = entity.Status;
        if (oldState == newState) return;
        entity.Status = newState;
        Reindex(_byStatus, id, oldState, newState);
    }

    /// <summary>按状态获取计划实体列表（O(1) 索引查找）。</summary>
    public IEnumerable<PlanEntity> GetByStatus(PlanStatus status) => _byStatus.GetValues(status, AsDictionary());
}
