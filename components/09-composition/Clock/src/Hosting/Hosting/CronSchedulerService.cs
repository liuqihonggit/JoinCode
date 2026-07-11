
namespace Core.Hosting;

/// <summary>
/// Cron 调度器服务 - 后台运行 Cron 任务调度
/// </summary>
public sealed partial class CronSchedulerService : IWorkflowService, IAsyncDisposable
{
    private readonly ICronTaskStore _taskStore;
    private readonly ServiceMessageBus _messageBus;
    private readonly INotificationService? _notificationService;
    [Inject] private readonly ILogger<CronSchedulerService>? _logger;
    private CronScheduler? _scheduler;
    private CancellationTokenSource? _cts;

    public string ServiceName => "CronScheduler";

    public ServiceStatus Status { get; private set; } = ServiceStatus.Stopped;

    public CronSchedulerService(
        ICronTaskStore taskStore,
        ServiceMessageBus messageBus,
        
        INotificationService? notificationService = null,
        ILogger<CronSchedulerService>? logger = null)
    {
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Status == ServiceStatus.Running)
        {
            _logger?.LogWarning(L.T(StringKey.CronSchedulerAlreadyRunning));
            return;
        }

        Status = ServiceStatus.Starting;
        _logger?.LogInformation(L.T(StringKey.CronSchedulerStarting));

        _cts = new CancellationTokenSource();

        var options = new CronSchedulerOptions
        {
            OnFire = async task =>
            {
                _logger?.LogInformation(L.T(StringKey.CronSchedulerTaskFired), task.Id, task.Prompt);

                await _messageBus.PublishAsync(ServiceMessage.Create(
                    ServiceMessageType.CronTaskFired.ToValue(),
                    ServiceName,
                    new CronTaskFiredEvent
                    {
                        TaskId = task.Id,
                        Prompt = task.Prompt,
                        CronExpression = task.CronExpression
                    })).ConfigureAwait(false);

                if (_notificationService != null)
                {
                    await _notificationService.NotifyAsync(
                        L.T(StringKey.CronSchedulerTaskNotificationTitle),
                        $"[{task.Id}] {task.Prompt}").ConfigureAwait(false);
                }
            },
            JitterConfig = CronJitterConfig.Default
        };

        _scheduler = new CronScheduler(options, _taskStore);
        await _scheduler.StartAsync(cancellationToken).ConfigureAwait(false);

        Status = ServiceStatus.Running;
        _logger?.LogInformation(L.T(StringKey.CronSchedulerStarted));
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Status != ServiceStatus.Running)
        {
            return;
        }

        Status = ServiceStatus.Stopping;
        _logger?.LogInformation(L.T(StringKey.CronSchedulerStopping));

        _cts?.CancelAsync();

        if (_scheduler != null)
        {
            await _scheduler.StopAsync(cancellationToken).ConfigureAwait(false);
            await _scheduler.DisposeAsync().ConfigureAwait(false);
        }

        _scheduler = null;

        Status = ServiceStatus.Stopped;
        _logger?.LogInformation(L.T(StringKey.CronSchedulerStopped));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.CronSchedulerDisposeError));
        }

        _cts?.Dispose();
    }
}

/// <summary>
/// Cron 任务触发事件
/// </summary>
public sealed record CronTaskFiredEvent
{
    public required string TaskId { get; init; }
    public required string Prompt { get; init; }
    public required string CronExpression { get; init; }
}
