
namespace Core.Hosting;

/// <summary>
/// 服务主机 - 管理所有工作流服务的生命周期
/// </summary>
public sealed partial class ServiceHost : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, IWorkflowService> _services = new();
    private readonly ConcurrentDictionary<string, ServiceStatus> _serviceStatuses = new();
    [Inject] private readonly ILogger<ServiceHost>? _logger;
    private readonly CancellationTokenSource _hostCts = new();
    private bool _isRunning;

    public ServiceHost(ILogger<ServiceHost>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 服务状态变更事件
    /// </summary>
    public event EventHandler<ServiceEventArgs>? ServiceStatusChanged;

    /// <summary>
    /// 注册服务
    /// </summary>
    public void RegisterService(IWorkflowService service)
    {
        if (service == null)
            throw new ArgumentNullException(nameof(service));

        if (_services.TryAdd(service.ServiceName, service))
        {
            _serviceStatuses[service.ServiceName] = ServiceStatus.Stopped;
            _logger?.LogInformation("服务已注册: {ServiceName}", service.ServiceName);
        }
        else
        {
            throw new InvalidOperationException(L.T(StringKey.ServiceHostAlreadyRegistered, service.ServiceName));
        }
    }

    /// <summary>
    /// 启动所有服务
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _logger?.LogWarning(L.T(StringKey.ServiceHostAlreadyRunning));
            return;
        }

        _isRunning = true;
        _logger?.LogInformation(L.T(StringKey.ServiceHostStarting));

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_hostCts.Token, cancellationToken);

        var startTasks = _services.Select(async kvp =>
        {
            try
            {
                await StartServiceAsync(kvp.Value, linkedCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, L.T(StringKey.ServiceHostStartFailed), kvp.Key);
                throw;
            }
        });
        await Task.WhenAll(startTasks).ConfigureAwait(false);

        _logger?.LogInformation(L.T(StringKey.ServiceHostStarted), _services.Count);
    }

    /// <summary>
    /// 停止所有服务
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            _logger?.LogWarning(L.T(StringKey.ServiceHostNotRunning));
            return;
        }

        _logger?.LogInformation(L.T(StringKey.ServiceHostStopping));
        await _hostCts.CancelAsync().ConfigureAwait(false);

        using var linkedCts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromSeconds(30)); // 30秒超时

        // 反向停止服务（按注册顺序的逆序）
        var services = _services.Values.Reverse().ToList();

        var stopTasks = services.Select(async service =>
        {
            try
            {
                await StopServiceAsync(service, linkedCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, L.T(StringKey.ServiceHostStopError), service.ServiceName);
            }
        });
        await Task.WhenAll(stopTasks).ConfigureAwait(false);

        _isRunning = false;
        _logger?.LogInformation(L.T(StringKey.ServiceHostStopped));
    }

    /// <summary>
    /// 启动特定服务
    /// </summary>
    public async Task<bool> StartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (!_services.TryGetValue(serviceName, out var service))
        {
            _logger?.LogWarning(L.T(StringKey.ServiceHostNotFound), serviceName);
            return false;
        }

        await StartServiceAsync(service, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 停止特定服务
    /// </summary>
    public async Task<bool> StopServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (!_services.TryGetValue(serviceName, out var service))
        {
            _logger?.LogWarning(L.T(StringKey.ServiceHostNotFound), serviceName);
            return false;
        }

        await StopServiceAsync(service, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 获取服务状态
    /// </summary>
    public ServiceStatus? GetServiceStatus(string serviceName)
    {
        return _serviceStatuses.TryGetValue(serviceName, out var status) ? status : null;
    }

    /// <summary>
    /// 获取所有服务状态
    /// </summary>
    public IReadOnlyDictionary<string, ServiceStatus> GetAllServiceStatuses()
    {
        return _serviceStatuses.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// 检查服务主机是否正在运行
    /// </summary>
    public bool IsRunning => _isRunning;

    private async Task StartServiceAsync(IWorkflowService service, CancellationToken cancellationToken)
    {
        var oldStatus = _serviceStatuses[service.ServiceName];

        try
        {
            _logger?.LogInformation(L.T(StringKey.ServiceHostStartingService), service.ServiceName);
            _serviceStatuses[service.ServiceName] = ServiceStatus.Starting;

            await service.StartAsync(cancellationToken).ConfigureAwait(false);

            _serviceStatuses[service.ServiceName] = ServiceStatus.Running;
            OnServiceStatusChanged(service.ServiceName, oldStatus, ServiceStatus.Running);

            _logger?.LogInformation(L.T(StringKey.ServiceHostServiceStarted), service.ServiceName);
        }
        catch (Exception ex)
        {
            _serviceStatuses[service.ServiceName] = ServiceStatus.Failed;
            OnServiceStatusChanged(service.ServiceName, oldStatus, ServiceStatus.Failed, exception: ex);
            throw;
        }
    }

    private async Task StopServiceAsync(IWorkflowService service, CancellationToken cancellationToken)
    {
        var oldStatus = _serviceStatuses[service.ServiceName];

        if (oldStatus == ServiceStatus.Stopped)
            return;

        try
        {
            _logger?.LogInformation(L.T(StringKey.ServiceHostStoppingService), service.ServiceName);
            _serviceStatuses[service.ServiceName] = ServiceStatus.Stopping;

            await service.StopAsync(cancellationToken).ConfigureAwait(false);

            _serviceStatuses[service.ServiceName] = ServiceStatus.Stopped;
            OnServiceStatusChanged(service.ServiceName, oldStatus, ServiceStatus.Stopped);

            _logger?.LogInformation(L.T(StringKey.ServiceHostServiceStopped), service.ServiceName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ServiceHostStopFailed), service.ServiceName);
            _serviceStatuses[service.ServiceName] = ServiceStatus.Failed;
            OnServiceStatusChanged(service.ServiceName, oldStatus, ServiceStatus.Failed, exception: ex);
            throw;
        }
    }

    private void OnServiceStatusChanged(string serviceName, ServiceStatus oldStatus, ServiceStatus newStatus, string? message = null, Exception? exception = null)
    {
        ServiceStatusChanged?.Invoke(this, new ServiceEventArgs
        {
            ServiceName = serviceName,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Message = message,
            Exception = exception
        });
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _hostCts.Dispose();
    }
}
