
namespace Core.Hosting;

/// <summary>
/// Workflow 应用程序 - 统一管理和启动所有服务
/// </summary>
public sealed partial class WorkflowApplication : IAsyncDisposable
{
    private readonly ServiceHost _serviceHost;
    private readonly ServiceMessageBus _messageBus;
    private readonly ICronTaskStore? _cronTaskStore;
    private readonly INotificationService? _notificationService;
    [Inject] private readonly ILogger<CronSchedulerService>? _cronLogger;
    [Inject] private readonly ILogger<WorkflowApplication>? _logger;
    [Inject] private readonly IClockService _clock;
    private DateTime _startedAt;

    public WorkflowApplication(
        ILogger<ServiceHost>? hostLogger = null,
        ICronTaskStore? cronTaskStore = null,
        
        INotificationService? notificationService = null,
        ILogger<CronSchedulerService>? cronLogger = null,
        ILogger<WorkflowApplication>? logger = null,
        IClockService? clock = null)
    {
        _cronTaskStore = cronTaskStore;
        _notificationService = notificationService;
        _cronLogger = cronLogger;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;

        _serviceHost = new ServiceHost(hostLogger);

        _messageBus = new ServiceMessageBus();

        _serviceHost.ServiceStatusChanged += OnServiceStatusChanged;
    }

    /// <summary>
    /// 消息总线
    /// </summary>
    public ServiceMessageBus MessageBus => _messageBus;

    /// <summary>
    /// 服务主机
    /// </summary>
    public ServiceHost ServiceHost => _serviceHost;

    /// <summary>
    /// 初始化并注册所有服务
    /// </summary>
    public void Initialize()
    {
        _logger?.LogInformation(L.T(StringKey.WorkflowAppInitializing));

        if (_cronTaskStore is not null)
        {
            var cronService = new CronSchedulerService(
                _cronTaskStore,
                _messageBus,
                _notificationService,
                _cronLogger);

            _serviceHost.RegisterService(cronService);
            _logger?.LogInformation(L.T(StringKey.WorkflowAppCronRegistered));
        }

        // 可以在这里注册更多服务...

        _logger?.LogInformation(L.T(StringKey.WorkflowAppInitialized), _serviceHost.GetAllServiceStatuses().Count);
    }

    /// <summary>
    /// 启动应用程序
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(L.T(StringKey.WorkflowAppStarting));

        _startedAt = _clock.GetUtcNow();

        // 启动服务主机
        await _serviceHost.StartAsync(cancellationToken).ConfigureAwait(false);

        // 发布系统启动消息
        await _messageBus.PublishAsync(ServiceMessage.Create(
            ServiceMessageType.SystemStarted.ToValue(),
            "WorkflowApplication",
            new { StartTime = _clock.GetUtcNow() })).ConfigureAwait(false);

        _logger?.LogInformation(L.T(StringKey.WorkflowAppStarted));
    }

    /// <summary>
    /// 停止应用程序
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(L.T(StringKey.WorkflowAppStopping));

        // 发布系统停止消息
        await _messageBus.PublishAsync(ServiceMessage.Create(
            ServiceMessageType.SystemStopped.ToValue(),
            "WorkflowApplication",
            new { StopTime = _clock.GetUtcNow() })).ConfigureAwait(false);

        // 停止服务主机
        await _serviceHost.StopAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation(L.T(StringKey.WorkflowAppStopped));
    }

    /// <summary>
    /// 运行应用程序直到取消
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 等待取消信号
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        finally
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 获取应用程序状态报告
    /// </summary>
    public ApplicationStatusReport GetStatusReport()
    {
        var serviceStatuses = _serviceHost.GetAllServiceStatuses();

        return new ApplicationStatusReport
        {
            IsRunning = _serviceHost.IsRunning,
            ServiceCount = serviceStatuses.Count,
            RunningServices = serviceStatuses.Count(s => s.Value == ServiceStatus.Running),
            FailedServices = serviceStatuses.Count(s => s.Value == ServiceStatus.Failed),
            ServiceStatuses = serviceStatuses.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToStatusName()),
            Uptime = _startedAt
        };
    }

    private void OnServiceStatusChanged(object? sender, ServiceEventArgs e)
    {
        _logger?.LogInformation(L.T(StringKey.WorkflowAppStatusChanged),
            e.ServiceName,
            e.OldStatus,
            e.NewStatus);

        // 发布服务状态变更消息
        _ = _messageBus.PublishAsync(ServiceMessage.Create(
            ServiceMessageType.ServiceStatusChanged.ToValue(),
            "WorkflowApplication",
            new ServiceStatusChangePayload
            {
                ServiceName = e.ServiceName,
                OldStatus = e.OldStatus.ToStatusName(),
                NewStatus = e.NewStatus.ToStatusName(),
                Message = e.Message,
                ErrorMessage = e.Exception?.Message
            }), CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _serviceHost.ServiceStatusChanged -= OnServiceStatusChanged;
        await _serviceHost.DisposeAsync().ConfigureAwait(false);
        _messageBus.Dispose();
    }
}

/// <summary>
/// 应用程序状态报告
/// </summary>
public sealed record ApplicationStatusReport
{
    public required bool IsRunning { get; init; }
    public required int ServiceCount { get; init; }
    public required int RunningServices { get; init; }
    public required int FailedServices { get; init; }
    public required Dictionary<string, string> ServiceStatuses { get; init; }
    public DateTime Uptime { get; init; }
}

/// <summary>
/// 服务状态变更消息载荷
/// </summary>
public sealed partial class ServiceStatusChangePayload
{
    public required string ServiceName { get; init; }
    public required string OldStatus { get; init; }
    public required string NewStatus { get; init; }
    public string? Message { get; init; }
    public string? ErrorMessage { get; init; }
}
