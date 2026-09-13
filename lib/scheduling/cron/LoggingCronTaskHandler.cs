namespace Core.Scheduling.Cron;

/// <summary>
/// 默认 Cron 任务触发处理器 — 记录日志
/// </summary>
[Register(typeof(ICronTaskHandler), ServiceLifetime.Singleton)]
public sealed partial class LoggingCronTaskHandler : ServiceEntity, ICronTaskHandler
{
    private readonly ILogger<CronScheduler> _logger;

    /// <summary>
    /// 初始化日志 Cron 任务处理器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public LoggingCronTaskHandler(ILogger<CronScheduler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task OnFireAsync(CronTask task)
    {
        _logger.LogInformation("[Cron] Task {TaskId} fired: {Prompt}", task.Id, task.Prompt);
        return Task.CompletedTask;
    }
}
