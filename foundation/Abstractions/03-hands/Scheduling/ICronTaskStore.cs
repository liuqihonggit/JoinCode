namespace JoinCode.Abstractions.Interfaces.Scheduling;

/// <summary>
/// Cron 任务定义
/// </summary>
public sealed record CronTask
{
    /// <summary>
    /// 任务唯一标识符
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 5 字段 Cron 表达式（本地时间）
    /// </summary>
    public required string CronExpression { get; init; }

    /// <summary>
    /// 任务触发时执行的提示/命令
    /// </summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// 创建时间（Unix 时间戳毫秒）
    /// </summary>
    public required long CreatedAt { get; init; }

    /// <summary>
    /// 上次触发时间（Unix 时间戳毫秒）
    /// </summary>
    public long? LastFiredAt { get; set; }

    /// <summary>
    /// 是否为重复任务
    /// </summary>
    public bool IsRecurring { get; init; }

    /// <summary>
    /// 是否永久任务（不过期）
    /// </summary>
    public bool IsPermanent { get; init; }

    /// <summary>
    /// 是否持久化（false 表示仅内存存储）
    /// </summary>
    public bool IsDurable { get; init; } = true;

    /// <summary>
    /// 创建者 Agent ID（可选）
    /// </summary>
    public string? AgentId { get; init; }

    /// <summary>
    /// 检查任务是否已过期
    /// </summary>
    public bool IsExpired(long nowMs, long maxAgeMs)
    {
        if (maxAgeMs == 0 || IsPermanent) return false;
        return IsRecurring && (nowMs - CreatedAt) >= maxAgeMs;
    }
}

/// <summary>
/// Cron 任务存储接口
/// </summary>
public interface ICronTaskStore
{
    /// <summary>
    /// 获取所有任务
    /// </summary>
    Task<IReadOnlyList<CronTask>> GetAllTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加任务
    /// </summary>
    Task<CronTask> AddTaskAsync(CreateCronTaskRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除任务
    /// </summary>
    Task RemoveTasksAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// 标记任务已触发
    /// </summary>
    Task MarkTasksFiredAsync(IEnumerable<string> ids, long firedAt, CancellationToken cancellationToken = default);
}
