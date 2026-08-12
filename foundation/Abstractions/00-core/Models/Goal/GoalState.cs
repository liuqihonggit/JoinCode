
namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 目标状态数据模型
/// </summary>
public sealed class GoalState
{
    public string GoalId { get; init; } = string.Empty;
    public string Objective { get; init; } = string.Empty;
    public GoalStatus Status { get; set; } = GoalStatus.Pursuing;
    public List<string> Constraints { get; init; } = [];
    public int? TokenBudget { get; init; }
    public int? TurnBudget { get; init; }
    public int TokensUsed { get; set; }
    public int TurnsCompleted { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? PausedAt { get; set; }
    public DateTime? AchievedAt { get; set; }
    public GoalEvaluationResult? LastEvaluation { get; set; }
    public DateTime? StagnationAlertedAt { get; set; }

    /// <summary>会话隔离标识 — 标记目标所属会话，持久化按 {baseDir}/{sessionId}/{goalId}.json 隔离</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>持久化的对话历史 — 进程重启后可恢复 ChatHistory 上下文</summary>
    public List<ApiMessageDocument>? PersistedHistory { get; set; }

    public TimeSpan Elapsed => AchievedAt.HasValue
        ? AchievedAt.Value - CreatedAt
        : DateTime.UtcNow - CreatedAt;
}

/// <summary>
/// 目标评估结果
/// </summary>
public sealed record GoalEvaluationResult
{
    public required bool IsCompleted { get; init; }
    public required string Reason { get; init; }

    /// <summary>目标已完成</summary>
    public static GoalEvaluationResult Completed(string reason) => new() { IsCompleted = true, Reason = reason };

    /// <summary>目标未完成</summary>
    public static GoalEvaluationResult NotCompleted(string reason) => new() { IsCompleted = false, Reason = reason };
}
