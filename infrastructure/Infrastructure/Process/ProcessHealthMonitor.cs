namespace Infrastructure.Subprocess;

public sealed class ProcessHealthMonitor : IDisposable
{
    private readonly IInteractiveProcess _process;
    private readonly HealthCheckConfig _config;
    private readonly ILogger? _logger;
    private readonly Timer _timer;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _consecutiveFailures;
    private DateTimeOffset _lastCheckTime = DateTimeOffset.MinValue;
    private int _isDisposed;

    public bool IsHealthy { get; private set; } = true;

    public DateTimeOffset? LastCheckTime
    {
        get
        {
            if (_lastCheckTime == DateTimeOffset.MinValue) return null;
            return _lastCheckTime;
        }
    }

    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    public event EventHandler<ProcessUnhealthyEventArgs>? Unhealthy;

    public ProcessHealthMonitor(
        IInteractiveProcess process,
        HealthCheckConfig config,
        ILogger? logger = null)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;

        _timer = new Timer(
            _ => PerformCheck(),
            null,
            _config.Interval,
            _config.Interval);
    }

    private void PerformCheck()
    {
        if (Volatile.Read(ref _isDisposed) == 1) return;

        try
        {
            var isAlive = !_process.HasExited;
            _lastCheckTime = DateTimeOffset.UtcNow;

            if (isAlive)
            {
                var wasUnhealthy = !IsHealthy;
                Volatile.Write(ref _consecutiveFailures, 0);
                IsHealthy = true;

                if (wasUnhealthy)
                {
                    _logger?.LogInformation("[ProcessHealth] 进程 {Pid} 恢复健康", _process.Id);
                }
            }
            else
            {
                Interlocked.Increment(ref _consecutiveFailures);
                IsHealthy = false;

                _logger?.LogWarning("[ProcessHealth] 进程 {Pid} 已退出 (consecutiveFailures={Failures})",
                    _process.Id, ConsecutiveFailures);

                if (ConsecutiveFailures >= _config.FailureThreshold)
                {
                    Unhealthy?.Invoke(this, new ProcessUnhealthyEventArgs
                    {
                        ProcessId = _process.Id,
                        ConsecutiveFailures = ConsecutiveFailures,
                        Action = _config.Action,
                        Reason = "Process has exited",
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ProcessHealth] 健康检查异常");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;
        _timer.Dispose();
        _lock.Dispose();
    }
}

public sealed class ProcessUnhealthyEventArgs : EventArgs
{
    public required int ProcessId { get; init; }
    public required int ConsecutiveFailures { get; init; }
    public required UnhealthyAction Action { get; init; }
    public required string Reason { get; init; }
}
