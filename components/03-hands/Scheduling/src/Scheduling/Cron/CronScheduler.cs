
namespace Core.Scheduling.Cron;

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
/// Cron 调度器实现
/// </summary>
[Register(typeof(ICronScheduler))]
[Register(typeof(ICronSchedulerRef))]
public sealed class CronScheduler : ICronScheduler, ICronSchedulerRef, IAsyncDisposable
{
    private readonly CronSchedulerOptions _options;
    private readonly ICronTaskStore _taskStore;
    private readonly IClockService _clock;
    private readonly ConcurrentDictionary<string, long> _nextFireAt = new();
    private readonly ConcurrentDictionary<string, byte> _inFlight = new();
    private readonly Timer _timer;
    private volatile bool _refreshNeeded;

    private volatile bool _started;
    private volatile bool _disposed;

    public CronScheduler(CronSchedulerOptions options, ICronTaskStore taskStore, IClockService? clock = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _clock = clock ?? SystemClockService.Instance;
        _timer = new Timer(Check, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// DI 构造函数 — 通过 ICronTaskHandler 处理触发的任务
    /// </summary>
    public CronScheduler(ICronTaskHandler handler, ICronTaskStore taskStore, ILogger<CronScheduler>? logger = null, IClockService? clock = null)
        : this(new CronSchedulerOptions
        {
            OnFire = task => handler.OnFireAsync(task),
            JitterConfig = CronJitterConfig.Default
        }, taskStore, clock)
    {
    }

    /// <summary>
    /// 通知调度器任务已变更 — 对齐 TS setScheduledTasksEnabled(true)
    /// 清除 _nextFireAt 缓存，下一个 tick 会重新计算所有触发时间
    /// </summary>
    public void NotifyTaskChanged()
    {
        _refreshNeeded = true;
        _nextFireAt.Clear();
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_started) return Task.CompletedTask;

        _started = true;
        _timer.Change(0, _options.CheckIntervalMs);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        if (!_started) return Task.CompletedTask;

        _started = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public Task<long?> GetNextFireTimeAsync(CancellationToken ct = default)
    {
        long min = long.MaxValue;
        foreach (var time in _nextFireAt.Values)
        {
            if (time < min) min = time;
        }
        long? result = min == long.MaxValue ? null : min;
        return Task.FromResult(result);
    }

    private void Check(object? state)
    {
        if (!_started || _disposed) return;

        // 对齐 TS: NotifyTaskChanged 设置 _refreshNeeded，Check 时清除缓存
        _ = Interlocked.Exchange(ref _refreshNeeded, false);

        // 使用 Task.Run 避免在 Timer 线程上同步等待异步操作
        _ = Task.Run(async () =>
        {
            try
            {
                var now = _clock.GetUtcNowOffset().ToUnixTimeMilliseconds();
                var tasks = await _taskStore.GetAllTasksAsync().ConfigureAwait(false);
                var seen = new HashSet<string>();
                var firedRecurring = new List<string>();

                foreach (var task in tasks)
                {
                    if (_options.Filter != null && !_options.Filter(task)) continue;

                    seen.Add(task.Id);

                    if (_inFlight.ContainsKey(task.Id)) continue;

                    var next = GetNextFireTime(task, now);
                    if (next == null) continue;

                    if (now >= next)
                    {
                        FireTask(task, now, firedRecurring);
                    }
                }

                // 清理已不存在的任务
                var toRemove = _nextFireAt.Keys.Where(id => !seen.Contains(id)).ToList();
                foreach (var id in toRemove)
                {
                    _nextFireAt.TryRemove(id, out _);
                }

                // 批量更新重复任务的 lastFiredAt
                if (firedRecurring.Count > 0)
                {
                    await _taskStore.MarkTasksFiredAsync(firedRecurring, now).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // 记录错误但不停止调度器
                System.Diagnostics.Trace.WriteLine($"[CronScheduler] Check failed: {ex}");
            }
        });
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
                // 重复任务：重新调度
                var newNext = CronJitterHelper.JitteredNextCronRunMs(
                    task.CronExpression, now, task.Id, _options.JitterConfig);

                _nextFireAt[task.Id] = newNext ?? long.MaxValue;

                firedRecurring.Add(task.Id);
            }
            else
            {
                // 一次性任务或过期任务：删除
                _inFlight[task.Id] = 1;
                _nextFireAt.TryRemove(task.Id, out _);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _taskStore.RemoveTasksAsync([task.Id]).ConfigureAwait(false);
                    }
                    finally
                    {
                        _inFlight.TryRemove(task.Id, out _);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[CronScheduler] Fire task {task.Id} failed: {ex}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _timer.Dispose();
    }
}
