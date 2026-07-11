namespace Core.Scheduling.Cron;

/// <summary>
/// 默认 Cron 任务触发处理器 — 记录日志
/// </summary>
[Register]
public sealed class LoggingCronTaskHandler : ICronTaskHandler
{
    private readonly ILogger<CronScheduler> _logger;

    public LoggingCronTaskHandler(ILogger<CronScheduler> logger)
    {
        _logger = logger;
    }

    public Task OnFireAsync(CronTask task)
    {
        _logger.LogInformation("[Cron] Task {TaskId} fired: {Prompt}", task.Id, task.Prompt);
        return Task.CompletedTask;
    }
}
