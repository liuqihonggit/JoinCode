namespace Core.Scheduling.Cron;

/// <summary>
/// Cron 任务文件格式
/// </summary>
public sealed record CronTaskFile
{
    public List<CronTask> Tasks { get; init; } = new();
}
