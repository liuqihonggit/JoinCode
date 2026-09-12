namespace Infrastructure.Subprocess;

/// <summary>
/// 进程健康监控命令 — Actor 消息类型
/// </summary>
public interface IProcessHealthCommand;

public sealed record HealthCheckTickCmd : IProcessHealthCommand;

public sealed class ProcessHealthMonitor : ActorBase<IProcessHealthCommand, Unit>, IDisposable
{
    private readonly IInteractiveProcess _process;
    private readonly HealthCheckConfig _config;
    private readonly ILogger? _logger;
    private readonly Timer _timer;
    private int _isDisposed;

    private int _consecutiveFailures;
    private DateTimeOffset _lastCheckTime = DateTimeOffset.MinValue;
    private bool _isHealthy = true;

    public bool IsHealthy => Volatile.Read(ref _isHealthy);

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
        : base()
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;

        _timer = new Timer(
            _ => TrySend(new HealthCheckTickCmd()),
            null,
            _config.Interval,
            _config.Interval);
    }

    protected override ValueTask HandleAsync(IProcessHealthCommand command, CancellationToken ct)
    {
        if (command is HealthCheckTickCmd)
        {
            PerformCheck();
        }
        return ValueTask.CompletedTask;
    }

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogWarning(ex, "[ProcessHealth] 健康检查异常");
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
                var wasUnhealthy = !_isHealthy;
                Volatile.Write(ref _consecutiveFailures, 0);
                Volatile.Write(ref _isHealthy, true);

                if (wasUnhealthy)
                {
                    _logger?.LogInformation("[ProcessHealth] 进程 {Pid} 恢复健康", _process.Id);
                }
            }
            else
            {
                Interlocked.Increment(ref _consecutiveFailures);
                Volatile.Write(ref _isHealthy, false);

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
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _timer.Dispose();
        try
        {
            DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ProcessHealth] Dispose 超时");
        }
    }
}

public sealed class ProcessUnhealthyEventArgs : EventArgs
{
    public required int ProcessId { get; init; }
    public required int ConsecutiveFailures { get; init; }
    public required UnhealthyAction Action { get; init; }
    public required string Reason { get; init; }
}
