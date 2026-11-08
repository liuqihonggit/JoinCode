
namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 目标状态数据模型
/// </summary>
public sealed class GoalState {
    /// <summary>获取目标标识。</summary>
    public string GoalId { get; init; } = string.Empty;
    /// <summary>获取目标描述。</summary>
    public string Objective { get; init; } = string.Empty;
    /// <summary>获取或设置目标状态。</summary>
    public GoalStatus Status { get; set; } = GoalStatus.Pursuing;
    /// <summary>获取约束条件列表。</summary>
    public List<string> Constraints { get; init; } = [];
    /// <summary>获取令牌预算。</summary>
    public int? TokenBudget { get; init; }
    /// <summary>获取轮次预算。</summary>
    public int? TurnBudget { get; init; }
    /// <summary>获取或设置已用令牌数。</summary>
    public int TokensUsed { get; set; }
    /// <summary>获取或设置已完成轮次数。</summary>
    public int TurnsCompleted { get; set; }
    /// <summary>获取创建时间。</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>获取或设置暂停时间。</summary>
    public DateTime? PausedAt { get; set; }
    /// <summary>获取或设置达成时间。</summary>
    public DateTime? AchievedAt { get; set; }
    /// <summary>获取或设置最近一次评估结果。</summary>
    public GoalEvaluationResult? LastEvaluation { get; set; }
    /// <summary>获取或设置停滞告警时间。</summary>
    public DateTime? StagnationAlertedAt { get; set; }

    /// <summary>会话隔离标识 — 标记目标所属会话，持久化按 {baseDir}/{sessionId}/{goalId}.json 隔离</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>持久化的对话历史 — 进程重启后可恢复 ChatHistory 上下文</summary>
    public List<ApiMessageDocument> PersistedHistory { get; set; } = [];

    /// <summary>获取已耗时长。</summary>
    public TimeSpan Elapsed => AchievedAt.HasValue
        ? AchievedAt.Value - CreatedAt
        : DateTime.UtcNow - CreatedAt;
}

/// <summary>
/// 目标评估结果
/// </summary>
public sealed record GoalEvaluationResult {
    /// <summary>获取是否已完成。</summary>
    public required bool IsCompleted { get; init; }
    /// <summary>获取评估原因。</summary>
    public required string Reason { get; init; }

    /// <summary>目标已完成</summary>
    public static GoalEvaluationResult Completed(string reason) => new() { IsCompleted = true, Reason = reason };

    /// <summary>目标未完成</summary>
    public static GoalEvaluationResult NotCompleted(string reason) => new() { IsCompleted = false, Reason = reason };
}