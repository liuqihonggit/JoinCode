
namespace Core.Hosting;

/// <summary>
/// 服务主机 - 管理所有工作流服务的生命周期
/// </summary>
public sealed partial class ServiceHost : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ServiceEntry> _services = new();
    private readonly ILogger<ServiceHost>? _logger;
    private readonly CancellationTokenSource _hostCts = new();
    private bool _isRunning;
    private int _disposed;

    /// <summary>
    /// 构造 ServiceHost — 注入可选日志记录器
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
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
        ArgumentNullException.ThrowIfNull(service);

        if (_services.TryAdd(service.ServiceName, new ServiceEntry { Service = service }))
        {
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
        var entries = _services.Values.Reverse().ToList();

        var stopTasks = entries.Select(async entry =>
        {
            try
            {
                await StopServiceAsync(entry, linkedCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, L.T(StringKey.ServiceHostStopError), entry.Service.ServiceName);
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
        if (!_services.TryGetValue(serviceName, out var entry))
        {
            _logger?.LogWarning(L.T(StringKey.ServiceHostNotFound), serviceName);
            return false;
        }

        await StartServiceAsync(entry, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 停止特定服务
    /// </summary>
    public async Task<bool> StopServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (!_services.TryGetValue(serviceName, out var entry))
        {
            _logger?.LogWarning(L.T(StringKey.ServiceHostNotFound), serviceName);
            return false;
        }

        await StopServiceAsync(entry, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 获取服务状态
    /// </summary>
    public ServiceStatus? GetServiceStatus(string serviceName)
    {
        return _services.TryGetValue(serviceName, out var entry) ? entry.Status : null;
    }

    /// <summary>
    /// 获取所有服务状态
    /// </summary>
    public IReadOnlyDictionary<string, ServiceStatus> GetAllServiceStatuses()
    {
        return _services.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Status);
    }

    /// <summary>
    /// 检查服务主机是否正在运行
    /// </summary>
    public bool IsRunning => _isRunning;

    private async Task StartServiceAsync(ServiceEntry entry, CancellationToken cancellationToken)
    {
        var service = entry.Service;
        var oldStatus = entry.Status;

        try
        {
            _logger?.LogInformation(L.T(StringKey.ServiceHostStartingService), service.ServiceName);
            entry.Status = ServiceStatus.Starting;

            await service.StartAsync(cancellationToken).ConfigureAwait(false);

            entry.Status = ServiceStatus.Running;
            OnServiceStatusChanged(service.ServiceName, oldStatus, ServiceStatus.Running);

            _logger?.LogInformation(L.T(StringKey.ServiceHostServiceStarted), service.ServiceName);
        }
        catch (Exception ex)
        {
            entry.Status = ServiceStatus.Failed;
            OnServiceStatusChanged(service.ServiceName, oldStatus, ServiceStatus.Failed, exception: ex);
            throw;
        }
    }

    private async Task StopServiceAsync(ServiceEntry entry, CancellationToken cancellationToken)
    {
        var service = entry.Service;
        var oldStatus = entry.Status;

        if (oldStatus == ServiceStatus.Stopped)
            return;

        try
        {
            _logger?.LogInformation(L.T(StringKey.ServiceHostStoppingService), service.ServiceName);
            entry.Status = ServiceStatus.Stopping;

            await service.StopAsync(cancellationToken).ConfigureAwait(false);

            entry.Status = ServiceStatus.Stopped;
            OnServiceStatusChanged(service.ServiceName, oldStatus, ServiceStatus.Stopped);

            _logger?.LogInformation(L.T(StringKey.ServiceHostServiceStopped), service.ServiceName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ServiceHostStopFailed), service.ServiceName);
            entry.Status = ServiceStatus.Failed;
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

    /// <summary>
    /// 异步释放 — 停止所有服务并释放主机取消令牌
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _hostCts.Dispose();
    }
}
