namespace Core.Scheduling.Cron;

/// <summary>
/// Cron 任务文件格式
/// </summary>
public sealed record CronTaskFile
{
    /// <summary>
    /// 文件中包含的 Cron 任务列表
    /// </summary>
    public List<CronTask> Tasks { get; init; } = new();
}
