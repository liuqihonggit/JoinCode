namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 目标实体 — 派生自 Entity，与 Agent 同套路
/// 代表运行时目标（区别于 GoalState，后者是数据模型）
/// ObjectId + 目标描述 + 创建时间 + 独立注册器 + 静态属性暴露
/// </summary>
public sealed class Goal : Entity
{
    public string Objective { get; }
    public GoalStatus Status { get; set; } = GoalStatus.Pursuing;
    public List<string> Constraints { get; init; } = [];
    public int? TokenBudget { get; init; }
    public int TokensUsed { get; set; }
    public int TurnsCompleted { get; set; }
    public DateTime? PausedAt { get; set; }
    public DateTime? AchievedAt { get; set; }
    public GoalEvaluationResult? LastEvaluation { get; set; }
    public DateTime? StagnationAlertedAt { get; set; }

    /// <summary>
    /// 全局唯一 Goal 注册器 — 静态属性暴露，无需DI
    /// </summary>
    public static GoalRegistry Registry { get; } = new();

    public Goal(
        string objective,
        List<string>? constraints = null,
        int? tokenBudget = null,
        string? displayName = null)
        : base(ObjectType.Goal, displayName ?? objective)
    {
        Objective = objective;
        Constraints = constraints ?? [];
        TokenBudget = tokenBudget;

        Registry.Add(ObjectId, this);
    }

    /// <summary>
    /// 惰性释放 — 持久化服务确认数据全部写入后才调用
    /// </summary>
    protected override void OnDispose()
    {
        Registry.Remove(ObjectId);
    }

    /// <summary>
    /// 转换为 GoalState DTO（供 IGoalEngine 等消费方使用）
    /// </summary>
    public GoalState ToGoalState() => new()
    {
        GoalId = UniqueId,
        Objective = Objective,
        Status = Status,
        Constraints = Constraints,
        TokenBudget = TokenBudget,
        TokensUsed = TokensUsed,
        TurnsCompleted = TurnsCompleted,
        CreatedAt = CreatedAt,
        PausedAt = PausedAt,
        AchievedAt = AchievedAt,
        LastEvaluation = LastEvaluation,
        StagnationAlertedAt = StagnationAlertedAt
    };

    /// <summary>
    /// 从 GoalState DTO 创建 Goal 实体（反持久化）
    /// </summary>
    public static Goal FromGoalState(GoalState state) => new(
        objective: state.Objective,
        constraints: state.Constraints,
        tokenBudget: state.TokenBudget,
        displayName: state.GoalId)
    {
        Status = state.Status,
        TokensUsed = state.TokensUsed,
        TurnsCompleted = state.TurnsCompleted,
        PausedAt = state.PausedAt,
        AchievedAt = state.AchievedAt,
        LastEvaluation = state.LastEvaluation,
        StagnationAlertedAt = state.StagnationAlertedAt
    };

    /// <summary>
    /// 已用时间
    /// </summary>
    public TimeSpan Elapsed => AchievedAt.HasValue
        ? AchievedAt.Value - CreatedAt
        : DateTime.UtcNow - CreatedAt;
}
