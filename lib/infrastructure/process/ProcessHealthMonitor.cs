namespace Infrastructure.Subprocess;

/// <summary>
/// 进程健康监控命令 — Actor 消息类型
/// </summary>
public interface IProcessHealthCommand;

/// <summary>
/// 健康检查心跳命令 — 定时触发一次健康检查
/// </summary>
public sealed record HealthCheckTickCmd : IProcessHealthCommand;

/// <summary>
/// 进程健康监控器 — 基于定时器周期性检查交互式进程存活状态，连续失败达阈值时触发 Unhealthy 事件
/// </summary>
public sealed class ProcessHealthMonitor : ActorBase<IProcessHealthCommand, Unit>
{
    private readonly IInteractiveProcess _process;
    private readonly HealthCheckConfig _config;
    private readonly ILogger? _logger;
    private readonly Timer _timer;
    private int _isDisposed;

    private int _consecutiveFailures;
    private DateTimeOffset _lastCheckTime = DateTimeOffset.MinValue;
    private bool _isHealthy = true;

    /// <summary>
    /// 获取进程当前是否健康
    /// </summary>
    public bool IsHealthy => Volatile.Read(ref _isHealthy);

    /// <summary>
    /// 获取最近一次健康检查时间（尚未检查过则返回 null）
    /// </summary>
    public DateTimeOffset? LastCheckTime
    {
        get
        {
            if (_lastCheckTime == DateTimeOffset.MinValue) return null;
            return _lastCheckTime;
        }
    }

    /// <summary>
    /// 获取连续失败次数 — 进程存活时归零
    /// </summary>
    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    /// <summary>
    /// 进程不健康事件 — 连续失败次数达阈值时触发
    /// </summary>
    public event EventHandler<ProcessUnhealthyEventArgs>? Unhealthy;

    /// <summary>
    /// 构造进程健康监控器
    /// </summary>
    /// <param name="process">被监控的交互式进程</param>
    /// <param name="config">健康检查配置</param>
    /// <param name="logger">日志记录器（可选）</param>
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

    /// <summary>处理健康检查命令</summary>
    /// <param name="command">健康检查命令</param>
    /// <param name="ct">取消令牌</param>
    protected override ValueTask HandleAsync(IProcessHealthCommand command, CancellationToken ct)
    {
        if (command is HealthCheckTickCmd)
        {
            PerformCheck();
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>消费者异常回调 — 记录日志</summary>
    /// <param name="ex">异常对象</param>
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

    /// <summary>
    /// 异步释放监控器 — 停止定时器并等待 Actor 队列排空
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _timer.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// 进程不健康事件参数 — 携带进程 ID、连续失败次数、处置动作和原因
/// </summary>
public sealed class ProcessUnhealthyEventArgs : EventArgs
{
    /// <summary>不健康的进程 ID</summary>
    public required int ProcessId { get; init; }
    /// <summary>连续失败次数</summary>
    public required int ConsecutiveFailures { get; init; }
    /// <summary>处置动作</summary>
    public required UnhealthyAction Action { get; init; }
    /// <summary>不健康原因描述</summary>
    public required string Reason { get; init; }
}
