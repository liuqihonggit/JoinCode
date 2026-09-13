
namespace Core.Hosting;

/// <summary>
/// Cron 调度器服务 - 后台运行 Cron 任务调度
/// </summary>
public sealed partial class CronSchedulerService : IWorkflowService, IAsyncDisposable
{
    private readonly ICronTaskStore _taskStore;
    private readonly ServiceMessageBus _messageBus;
    private readonly INotificationService? _notificationService;
    private readonly ILogger<CronSchedulerService>? _logger;
    private CronScheduler? _scheduler;
    private CancellationTokenSource? _cts;
    private int _disposed;

    /// <summary>服务名称</summary>
    public string ServiceName => "CronScheduler";

    /// <summary>服务当前状态</summary>
    public ServiceStatus Status { get; private set; } = ServiceStatus.Stopped;

    /// <summary>
    /// 构造 CronSchedulerService — 注入任务存储、消息总线、可选通知服务与日志记录器
    /// </summary>
    /// <param name="taskStore">Cron 任务存储</param>
    /// <param name="messageBus">服务消息总线</param>
    /// <param name="notificationService">可选通知服务</param>
    /// <param name="logger">可选日志记录器</param>
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

    /// <summary>
    /// 启动 Cron 调度服务 — 初始化调度器并开始后台调度
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
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

    /// <summary>
    /// 停止 Cron 调度服务 — 停止并释放调度器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
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

    /// <summary>
    /// 异步释放 — 停止服务并释放取消令牌
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
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
    /// <summary>任务 ID</summary>
    public required string TaskId { get; init; }
    /// <summary>任务提示词</summary>
    public required string Prompt { get; init; }
    /// <summary>Cron 表达式</summary>
    public required string CronExpression { get; init; }
}
