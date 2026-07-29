namespace JoinCode.Abstractions.Interfaces.Scheduling;

/// <summary>
/// 创建 Cron 任务的参数
/// </summary>
public sealed record CreateCronTaskRequest
{
    /// <summary>
    /// Cron 表达式
    /// </summary>
    public required string CronExpression { get; init; }

    /// <summary>
    /// 任务提示/命令
    /// </summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// 是否为重复任务
    /// </summary>
    public bool IsRecurring { get; init; }

    /// <summary>
    /// 是否持久化
    /// </summary>
    public bool IsDurable { get; init; } = true;

    /// <summary>
    /// 创建者 Agent ID
    /// </summary>
    public string? AgentId { get; init; }
}
