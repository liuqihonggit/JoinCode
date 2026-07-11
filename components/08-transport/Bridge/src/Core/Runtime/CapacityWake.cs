
namespace Core.Bridge;

using JoinCode.Abstractions.Attributes;

/// <summary>
/// 容量唤醒选项 - 自动伸缩配置
/// </summary>
[Register]
public sealed partial class CapacityWakeOptions
{
    public const int DefaultMinInstances = 1;
    public const int DefaultMaxInstances = 5;
    public const int DefaultScaleUpThresholdPercent = 80;
    public const int DefaultScaleDownThresholdPercent = 20;
    public const int DefaultCheckIntervalMs = 5000;

    /// <summary>最小实例数</summary>
    [JsonPropertyName("minInstances")]
    public int MinInstances { get; init; } = DefaultMinInstances;

    /// <summary>最大实例数</summary>
    [JsonPropertyName("maxInstances")]
    public int MaxInstances { get; init; } = DefaultMaxInstances;

    /// <summary>扩容阈值百分比（0-100）</summary>
    [JsonPropertyName("scaleUpThreshold")]
    public int ScaleUpThreshold { get; init; } = DefaultScaleUpThresholdPercent;

    /// <summary>缩容阈值百分比（0-100）</summary>
    [JsonPropertyName("scaleDownThreshold")]
    public int ScaleDownThreshold { get; init; } = DefaultScaleDownThresholdPercent;

    /// <summary>检查间隔（毫秒）</summary>
    [JsonPropertyName("checkIntervalMs")]
    public int CheckIntervalMs { get; init; } = DefaultCheckIntervalMs;

    public CapacityWakeOptions() { }

    public CapacityWakeOptions(BridgeConfig config)
    {
        MinInstances = config.CapacityMinInstances;
        MaxInstances = config.CapacityMaxInstances;
        ScaleUpThreshold = (int)config.CapacityScaleUpThreshold;
        ScaleDownThreshold = (int)config.CapacityScaleDownThreshold;
    }
}

/// <summary>
/// 负载指标 - 当前系统负载快照
/// </summary>
public sealed partial class LoadMetrics
{
    /// <summary>活跃连接数</summary>
    [JsonPropertyName("activeConnections")]
    public int ActiveConnections { get; init; }

    /// <summary>待处理请求数</summary>
    [JsonPropertyName("pendingRequests")]
    public int PendingRequests { get; init; }

    /// <summary>CPU 使用率百分比（0-100）</summary>
    [JsonPropertyName("cpuUsagePercent")]
    public double CpuUsagePercent { get; init; }

    /// <summary>内存使用率百分比（0-100）</summary>
    [JsonPropertyName("memoryUsagePercent")]
    public double MemoryUsagePercent { get; init; }

    /// <summary>
    /// 计算综合负载百分比（0-100）
    /// </summary>
    public double CompositeLoadPercent =>
        (CpuUsagePercent * 0.4) + (MemoryUsagePercent * 0.3) +
        (Math.Min(ActiveConnections / 100.0, 1.0) * 100 * 0.2) +
        (Math.Min(PendingRequests / 50.0, 1.0) * 100 * 0.1);
}

/// <summary>
/// 容量变更事件参数
/// </summary>
public sealed partial class CapacityChangedEventArgs : EventArgs
{
    public int OldInstanceCount { get; }
    public int NewInstanceCount { get; }
    public LoadMetrics LoadMetrics { get; }

    public CapacityChangedEventArgs(int oldInstanceCount, int newInstanceCount, LoadMetrics loadMetrics)
    {
        OldInstanceCount = oldInstanceCount;
        NewInstanceCount = newInstanceCount;
        LoadMetrics = loadMetrics;
    }
}

/// <summary>
/// 容量唤醒服务 - 监控负载并自动伸缩实例数
/// 基于负载指标自动扩容/缩容，确保系统在合理容量范围内运行
/// </summary>
[Register]
public sealed partial class CapacityWakeService : IAsyncDisposable
{
    private readonly CapacityWakeOptions _options;
    [Inject] private readonly ILogger<CapacityWakeService>? _logger;
    private readonly SemaphoreSlim _stateLock;

    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private int _currentInstanceCount;
    private LoadMetrics _currentMetrics;
    private int _isDisposed;

    /// <summary>当前实例数</summary>
    public int CurrentInstanceCount => Volatile.Read(ref _currentInstanceCount);

    /// <summary>容量变更事件</summary>
    public event EventHandler<CapacityChangedEventArgs>? CapacityChanged;

    public CapacityWakeService(
        CapacityWakeOptions? options = null,
        ILogger<CapacityWakeService>? logger = null)
    {
        _options = options ?? new CapacityWakeOptions();
        _logger = logger;
        _stateLock = new SemaphoreSlim(1, 1);
        _currentInstanceCount = _options.MinInstances;
        _currentMetrics = new LoadMetrics();
    }

    /// <summary>
    /// 启动监控循环
    /// </summary>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_monitorTask is { IsCompleted: false })
            {
                _logger?.LogWarning("[CapacityWake] 监控已在运行");
                return;
            }

            _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _monitorTask = RunMonitorLoopAsync(_monitorCts.Token);
            _logger?.LogInformation("[CapacityWake] 监控已启动，当前实例数: {InstanceCount}", _currentInstanceCount);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// 停止监控循环
    /// </summary>
    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await (_monitorCts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        }
        finally
        {
            _stateLock.Release();
        }

        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _monitorCts?.Dispose();
        _monitorCts = null;
        _monitorTask = null;

        _logger?.LogInformation("[CapacityWake] 监控已停止");
    }

    /// <summary>
    /// 获取当前容量（实例数）
    /// </summary>
    public int GetCurrentCapacity() => Volatile.Read(ref _currentInstanceCount);

    /// <summary>
    /// 获取当前负载指标
    /// </summary>
    public LoadMetrics GetLoadMetrics() => Volatile.Read(ref _currentMetrics);

    /// <summary>
    /// 更新负载指标（供外部采集器调用）
    /// </summary>
    public void UpdateLoadMetrics(LoadMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        Volatile.Write(ref _currentMetrics, metrics);
    }

    /// <summary>
    /// 手动扩容一个实例
    /// </summary>
    public async Task ScaleUpAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var oldCount = _currentInstanceCount;
            if (oldCount >= _options.MaxInstances)
            {
                _logger?.LogWarning("[CapacityWake] 已达最大实例数 {MaxInstances}，无法扩容", _options.MaxInstances);
                return;
            }

            _currentInstanceCount = oldCount + 1;
            _logger?.LogInformation("[CapacityWake] 扩容: {OldCount} -> {NewCount}", oldCount, _currentInstanceCount);
            CapacityChanged?.Invoke(this, new CapacityChangedEventArgs(oldCount, _currentInstanceCount, _currentMetrics));
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// 手动缩容一个实例
    /// </summary>
    public async Task ScaleDownAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var oldCount = _currentInstanceCount;
            if (oldCount <= _options.MinInstances)
            {
                _logger?.LogWarning("[CapacityWake] 已达最小实例数 {MinInstances}，无法缩容", _options.MinInstances);
                return;
            }

            _currentInstanceCount = oldCount - 1;
            _logger?.LogInformation("[CapacityWake] 缩容: {OldCount} -> {NewCount}", oldCount, _currentInstanceCount);
            CapacityChanged?.Invoke(this, new CapacityChangedEventArgs(oldCount, _currentInstanceCount, _currentMetrics));
        }
        finally
        {
            _stateLock.Release();
        }
    }

    #region Signal Merger — 对齐 TS 端 capacityWake.ts

    private readonly SemaphoreSlim _wakeSemaphore = new(0, 1);
    private volatile int _wakeToken;

    /// <summary>
    /// 唤醒 at-capacity 睡眠 — 对齐 TS 端 capacityWake.wake()
    /// Abort 当前睡眠，让轮询循环立即重新检查工作
    /// </summary>
    public void WakeUp()
    {
        Interlocked.Exchange(ref _wakeToken, Interlocked.Increment(ref _wakeToken));
        _wakeSemaphore.Release();
    }

    /// <summary>
    /// 在 at-capacity 时等待唤醒 — 对齐 TS 端 capacityWake.signal() + sleepUntilCapacityWakes()
    /// 在给定超时时间内等待 WakeUp() 被调用，或直到达到超时
    /// </summary>
    /// <param name="timeout">最大等待时间</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>true 表示被唤醒，false 表示超时</returns>
    public async Task<bool> SleepUntilCapacityWakesAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            // 等待唤醒或超时
            var succeeded = await _wakeSemaphore.WaitAsync(timeout, ct).ConfigureAwait(false);

            // 如果成功，立即释放一个信号，保持 0→1→0 的计数
            // 下一次 WaitAsync 应该等待下一次 WakeUp
            if (succeeded)
            {
                // 释放一个信号，但下次 WaitAsync 应该重新等待
                // 所以这里不释放，直接返回
            }

            return succeeded;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    #endregion

    /// <summary>
    /// 监控循环 - 定期检查负载并自动伸缩
    /// </summary>
    private async Task RunMonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CheckIntervalMs, cancellationToken).ConfigureAwait(false);

                var metrics = Volatile.Read(ref _currentMetrics);
                var load = metrics.CompositeLoadPercent;

                if (load >= _options.ScaleUpThreshold)
                {
                    await ScaleUpAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (load <= _options.ScaleDownThreshold)
                {
                    await ScaleDownAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[CapacityWake] 监控循环错误");
            }
        }

        _logger?.LogDebug("[CapacityWake] 监控循环已退出");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        await StopMonitoringAsync().ConfigureAwait(false);
        _stateLock.Dispose();
    }
}
