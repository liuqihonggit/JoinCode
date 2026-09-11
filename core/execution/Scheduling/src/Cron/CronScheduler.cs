
namespace Core.Scheduling.Cron;

/// <summary>
/// Cron 调度器命令 — Actor 消息类型
/// </summary>
public interface ICronSchedulerCommand;

public sealed record CronStartCmd : ICronSchedulerCommand;
public sealed record CronStopCmd : ICronSchedulerCommand;
public sealed record CronCheckTickCmd : ICronSchedulerCommand;
public sealed record CronNotifyChangedCmd : ICronSchedulerCommand;
public sealed record CronGetNextFireCmd(TaskCompletionSource<long?> Tcs) : ICronSchedulerCommand;
public sealed record CronRemoveInFlightCmd(string TaskId) : ICronSchedulerCommand;

/// <summary>
/// Cron 调度器接口
/// </summary>
public interface ICronScheduler
{
    /// <summary>
    /// 启动调度器
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// 停止调度器
    /// </summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取下一个触发时间（Unix 时间戳毫秒），如果没有任务则返回 null
    /// </summary>
    Task<long?> GetNextFireTimeAsync(CancellationToken ct = default);
}

/// <summary>
/// Cron 任务触发处理器接口
/// </summary>
public interface ICronTaskHandler
{
    /// <summary>
    /// 处理触发的 Cron 任务
    /// </summary>
    Task OnFireAsync(CronTask task);
}

/// <summary>
/// Cron 调度器选项
/// </summary>
public sealed record CronSchedulerOptions
{
    /// <summary>
    /// 任务触发时的回调
    /// </summary>
    public Func<CronTask, Task>? OnFire { get; init; }

    /// <summary>
    /// 检查间隔（毫秒），默认 1000
    /// </summary>
    public int CheckIntervalMs { get; init; } = WorkflowConstants.Scheduling.CronCheckIntervalMs;

    /// <summary>
    /// 任务存储目录路径
    /// </summary>
    public string? TasksDirectory { get; init; }

    /// <summary>
    /// 抖动配置
    /// </summary>
    public CronJitterConfig JitterConfig { get; init; } = CronJitterConfig.Default;

    /// <summary>
    /// 任务过滤器
    /// </summary>
    public Func<CronTask, bool>? Filter { get; init; }
}

/// <summary>
/// Cron 调度器实现 — Actor 化：Consumer 线程独占 _nextFireAt/_inFlight，消除 ConcurrentDictionary。
/// <para>Timer 周期检查改为 TrySend(CronCheckTickCmd) 自消息，Consumer 串行处理。</para>
/// </summary>
[Register(typeof(ICronScheduler), ServiceLifetime.Singleton)]
[Register(typeof(ICronSchedulerRef), ServiceLifetime.Singleton)]
public sealed partial class CronScheduler : ActorBase<ICronSchedulerCommand, Unit>, ICronScheduler, ICronSchedulerRef
{
    private readonly CronSchedulerOptions _options;
    private readonly ICronTaskStore _taskStore;
    private readonly IClockService _clock;
    private readonly ILogger<CronScheduler>? _logger;
    private readonly Timer _timer;
    private int _disposed;

    private readonly Dictionary<string, long> _nextFireAt = new();
    private readonly HashSet<string> _inFlight = new();
    private bool _started;

    public CronScheduler(CronSchedulerOptions options, ICronTaskStore taskStore, IClockService? clock = null, ILogger<CronScheduler>? logger = null)
        : base()
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _clock = clock ?? SystemClockService.Instance;
        _logger = logger;
        _timer = new Timer(_ => TrySend(new CronCheckTickCmd()), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// DI 构造函数 — 通过 ICronTaskHandler 处理触发的任务
    /// </summary>
    public CronScheduler(ICronTaskHandler handler, ICronTaskStore taskStore, ILogger<CronScheduler>? logger = null, IClockService? clock = null)
        : this(new CronSchedulerOptions
        {
            OnFire = task => handler.OnFireAsync(task),
            JitterConfig = CronJitterConfig.Default
        }, taskStore, clock, logger)
    {
    }

    /// <summary>
    /// 通知调度器任务已变更 — 对齐 TS setScheduledTasksEnabled(true)
    /// 清除 _nextFireAt 缓存，下一个 tick 会重新计算所有触发时间
    /// </summary>
    public void NotifyTaskChanged()
    {
        TrySend(new CronNotifyChangedCmd());
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(CronScheduler));
        return SendAsync(new CronStartCmd(), ct).AsTask();
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        return SendAsync(new CronStopCmd(), ct).AsTask();
    }

    public async Task<long?> GetNextFireTimeAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<long?>();
        await SendAsync(new CronGetNextFireCmd(tcs), ct).ConfigureAwait(false);
        return await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    protected override async ValueTask HandleAsync(ICronSchedulerCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case CronStartCmd:
                if (_started) return;
                _started = true;
                _timer.Change(0, _options.CheckIntervalMs);
                _logger?.LogInformation("[CronScheduler] 已启动，检查间隔: {IntervalMs}ms", _options.CheckIntervalMs);
                break;

            case CronStopCmd:
                if (!_started) return;
                _started = false;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                break;

            case CronCheckTickCmd:
                await CheckAsync(ct).ConfigureAwait(false);
                break;

            case CronNotifyChangedCmd:
                _nextFireAt.Clear();
                break;

            case CronGetNextFireCmd(var tcs):
                long min = long.MaxValue;
                foreach (var time in _nextFireAt.Values)
                {
                    if (time < min) min = time;
                }
                tcs.SetResult(min == long.MaxValue ? null : min);
                break;

            case CronRemoveInFlightCmd(var taskId):
                _inFlight.Remove(taskId);
                break;
        }
    }

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[CronScheduler] 命令处理异常");
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        if (!_started || Volatile.Read(ref _disposed) != 0) return;

        var now = _clock.GetUtcNowOffset().ToUnixTimeMilliseconds();
        var tasks = await _taskStore.GetAllTasksAsync().ConfigureAwait(false);
        var seen = new HashSet<string>();
        var firedRecurring = new List<string>();

        foreach (var task in tasks)
        {
            if (_options.Filter != null && !_options.Filter(task)) continue;

            seen.Add(task.Id);

            if (_inFlight.Contains(task.Id)) continue;

            var next = GetNextFireTime(task, now);
            if (next == null) continue;

            if (now >= next)
            {
                FireTask(task, now, firedRecurring);
            }
        }

        var toRemove = _nextFireAt.Keys.Where(id => !seen.Contains(id)).ToList();
        foreach (var id in toRemove)
        {
            _nextFireAt.Remove(id);
        }

        if (firedRecurring.Count > 0)
        {
            try
            {
                await _taskStore.MarkTasksFiredAsync(firedRecurring, now).ConfigureAwait(false);
            }
            catch (Exception markEx)
            {
                _logger?.LogWarning(markEx, "[CronScheduler] MarkTasksFiredAsync 失败");
            }
        }
    }

    private long? GetNextFireTime(CronTask task, long now)
    {
        if (_nextFireAt.TryGetValue(task.Id, out var cached))
        {
            return cached;
        }

        long? next;
        if (task.IsRecurring)
        {
            next = CronJitterHelper.JitteredNextCronRunMs(
                task.CronExpression,
                task.LastFiredAt ?? task.CreatedAt,
                task.Id,
                _options.JitterConfig);
        }
        else
        {
            next = CronJitterHelper.OneShotJitteredNextCronRunMs(
                task.CronExpression,
                task.CreatedAt,
                task.Id,
                _options.JitterConfig);
        }

        if (next != null)
        {
            _nextFireAt[task.Id] = next.Value;
        }

        return next;
    }

    private void FireTask(CronTask task, long now, List<string> firedRecurring)
    {
        try
        {
            _ = _options.OnFire?.Invoke(task);

            if (task.IsRecurring && !task.IsExpired(now, _options.JitterConfig.RecurringMaxAgeMs))
            {
                var newNext = CronJitterHelper.JitteredNextCronRunMs(
                    task.CronExpression, now, task.Id, _options.JitterConfig);

                _nextFireAt[task.Id] = newNext ?? long.MaxValue;
                firedRecurring.Add(task.Id);
            }
            else
            {
                _inFlight.Add(task.Id);
                _nextFireAt.Remove(task.Id);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _taskStore.RemoveTasksAsync([task.Id]).ConfigureAwait(false);
                    }
                    finally
                    {
                        TrySend(new CronRemoveInFlightCmd(task.Id));
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[CronScheduler] 触发任务 {TaskId} 失败", task.Id);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        await base.DisposeAsync().ConfigureAwait(false);
        _timer.Dispose();
    }
}
