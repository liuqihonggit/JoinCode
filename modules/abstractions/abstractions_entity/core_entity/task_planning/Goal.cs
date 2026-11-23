namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 目标实体 — 派生自 Entity，与 Agent 同套路
/// 代表运行时目标（区别于 GoalState，后者是数据模型）
/// ObjectId + 目标描述 + 创建时间 + 独立注册器 + 静态属性暴露
/// </summary>
public sealed class Goal : Entity {
    /// <summary>获取目标描述。</summary>
    public string Objective { get; }
    /// <summary>获取或设置目标状态。</summary>
    public GoalStatus Status { get; set; } = GoalStatus.Pursuing;
    /// <summary>获取约束条件列表。</summary>
    public List<string> Constraints { get; init; } = [];
    /// <summary>获取 Token 预算。</summary>
    public int? TokenBudget { get; init; }
    /// <summary>获取或设置已用 Token 数。</summary>
    public int TokensUsed { get; set; }
    /// <summary>获取或设置已完成回合数。</summary>
    public int TurnsCompleted { get; set; }
    /// <summary>获取或设置暂停时间。</summary>
    public DateTime? PausedAt { get; set; }
    /// <summary>获取或设置达成时间。</summary>
    public DateTime? AchievedAt { get; set; }
    /// <summary>获取或设置最后评估结果。</summary>
    public GoalEvaluationResult? LastEvaluation { get; set; }
    /// <summary>获取或设置停滞告警时间。</summary>
    public DateTime? StagnationAlertedAt { get; set; }

    /// <summary>
    /// 全局唯一 Goal 注册器 — 静态属性暴露，无需DI
    /// </summary>
    public static GoalRegistry Registry { get; } = new();

    /// <summary>构造目标实体。</summary>
    /// <param name="objective">目标描述。</param>
    /// <param name="constraints">约束条件列表。</param>
    /// <param name="tokenBudget">Token 预算。</param>
    /// <param name="displayName">显示名称。</param>
    /// <param name="sessionId">会话标识。</param>
    public Goal(
        string objective,
        List<string>? constraints = null,
        int? tokenBudget = null,
        string? displayName = null,
        ObjectId sessionId = default)
        : base(ObjectType.Goal, sessionId, displayName ?? objective) {
        Objective = objective;
        Constraints = constraints ?? [];
        TokenBudget = tokenBudget;

        Registry.Add(ObjectId, this);
    }

    /// <summary>
    /// 惰性释放 — 持久化服务确认数据全部写入后才调用
    /// </summary>
    public override void Dispose() {
        Registry.Remove(ObjectId);
        base.Dispose();
    }

    /// <summary>
    /// 转换为 GoalState DTO（供 IGoalEngine 等消费方使用）
    /// </summary>
    public GoalState ToGoalState() => new() {
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
    public static Goal FromGoalState(GoalState state, ObjectId sessionId = default) => new(
        objective: state.Objective,
        constraints: state.Constraints,
        tokenBudget: state.TokenBudget,
        displayName: state.GoalId,
        sessionId: sessionId) {
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

    /// <summary>
    /// 跨会话深拷贝 — 新 ObjectId + 目标会话，深拷贝所有字段
    /// </summary>
    public override Entity Clone(CloneContext context) {
        var cloned = new Goal(
            objective: Objective,
            constraints: new List<string>(Constraints),
            tokenBudget: TokenBudget,
            displayName: DisplayName,
            sessionId: context.TargetSessionId) {
            Status = Status,
            TokensUsed = TokensUsed,
            TurnsCompleted = TurnsCompleted,
            PausedAt = PausedAt,
            AchievedAt = AchievedAt,
            LastEvaluation = LastEvaluation,
            StagnationAlertedAt = StagnationAlertedAt
        };
        context.Map(ObjectId, cloned.ObjectId);
        return cloned;
    }
}