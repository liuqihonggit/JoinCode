
namespace Core.Hosting;

/// <summary>
/// Workflow 应用程序 - 统一管理和启动所有服务
/// </summary>
public sealed partial class WorkflowApplication : IAsyncDisposable {
    private readonly ServiceHost _serviceHost;
    private readonly ServiceMessageBus _messageBus;
    private readonly ICronTaskStore? _cronTaskStore;
    private readonly INotificationService? _notificationService;
    private readonly ILogger<CronSchedulerService>? _cronLogger;
    private readonly ILogger<WorkflowApplication>? _logger;
    private readonly IClockService _clock;
    private DateTime _startedAt;
    private int _disposed;

    /// <summary>
    /// 构造 WorkflowApplication — 注入可选日志、Cron 任务存储、通知服务与时钟
    /// </summary>
    /// <param name="hostLogger">服务主机日志记录器，可选</param>
    /// <param name="cronTaskStore">Cron 任务存储，可选</param>
    /// <param name="notificationService">通知服务，可选</param>
    /// <param name="cronLogger">Cron 调度器日志记录器，可选</param>
    /// <param name="logger">应用程序日志记录器，可选</param>
    /// <param name="clock">时钟服务，可选，缺省使用系统时钟</param>
    public WorkflowApplication(
        ILogger<ServiceHost>? hostLogger = null,
        ICronTaskStore? cronTaskStore = null,

        INotificationService? notificationService = null,
        ILogger<CronSchedulerService>? cronLogger = null,
        ILogger<WorkflowApplication>? logger = null,
        IClockService? clock = null) {
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
    public void Initialize() {
        _logger?.LogInformation(L.T(StringKey.WorkflowAppInitializing));

        if (_cronTaskStore is not null) {
            _serviceHost.RegisterService(new CronSchedulerService(
                _cronTaskStore,
                _messageBus,
                _notificationService,
                _cronLogger));

            _logger?.LogInformation(L.T(StringKey.WorkflowAppCronRegistered));
        }

        // 可以在这里注册更多服务...

        _logger?.LogInformation(L.T(StringKey.WorkflowAppInitialized), _serviceHost.GetAllServiceStatuses().Count);
    }

    /// <summary>
    /// 启动应用程序
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default) {
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
    public async Task StopAsync(CancellationToken cancellationToken = default) {
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
    public async Task RunAsync(CancellationToken cancellationToken = default) {
        await StartAsync(cancellationToken).ConfigureAwait(false);

        try {
            // 等待取消信号
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            // 正常取消
        } finally {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 获取应用程序状态报告
    /// </summary>
    public ApplicationStatusReport GetStatusReport() {
        var serviceStatuses = _serviceHost.GetAllServiceStatuses();

        return new ApplicationStatusReport {
            IsRunning = _serviceHost.IsRunning,
            ServiceCount = serviceStatuses.Count,
            RunningServices = serviceStatuses.Count(s => s.Value == ServiceStatus.Running),
            FailedServices = serviceStatuses.Count(s => s.Value == ServiceStatus.Failed),
            ServiceStatuses = serviceStatuses.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToStatusName()),
            Uptime = _startedAt
        };
    }

    private void OnServiceStatusChanged(object? sender, ServiceEventArgs e) {
        _logger?.LogInformation(L.T(StringKey.WorkflowAppStatusChanged),
            e.ServiceName,
            e.OldStatus,
            e.NewStatus);

        // 发布服务状态变更消息
        _ = _messageBus.PublishAsync(ServiceMessage.Create(
            ServiceMessageType.ServiceStatusChanged.ToValue(),
            "WorkflowApplication",
            new ServiceStatusChangePayload {
                ServiceName = e.ServiceName,
                OldStatus = e.OldStatus.ToStatusName(),
                NewStatus = e.NewStatus.ToStatusName(),
                Message = e.Message,
                ErrorMessage = e.Exception?.Message
            }), CancellationToken.None);
    }

    /// <summary>
    /// 异步释放 — 停止应用、解绑事件并释放服务主机与消息总线
    /// </summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _serviceHost.ServiceStatusChanged -= OnServiceStatusChanged;
        await _serviceHost.DisposeAsync().ConfigureAwait(false);
        _messageBus.Dispose();
    }
}

/// <summary>
/// 应用程序状态报告
/// </summary>
public sealed record ApplicationStatusReport {
    /// <summary>应用是否正在运行</summary>
    public required bool IsRunning { get; init; }
    /// <summary>服务总数</summary>
    public required int ServiceCount { get; init; }
    /// <summary>运行中服务数</summary>
    public required int RunningServices { get; init; }
    /// <summary>失败服务数</summary>
    public required int FailedServices { get; init; }
    /// <summary>各服务状态名称字典</summary>
    public required Dictionary<string, string> ServiceStatuses { get; init; }
    /// <summary>启动时间</summary>
    public DateTime Uptime { get; init; }
}

/// <summary>
/// 服务状态变更消息载荷
/// </summary>
public sealed partial class ServiceStatusChangePayload {
    /// <summary>服务名称</summary>
    public required string ServiceName { get; init; }
    /// <summary>旧状态名称</summary>
    public required string OldStatus { get; init; }
    /// <summary>新状态名称</summary>
    public required string NewStatus { get; init; }
    /// <summary>附加消息，可选</summary>
    public string? Message { get; init; }
    /// <summary>错误消息，可选</summary>
    public string? ErrorMessage { get; init; }
}