namespace Core.Bridge;


/// <summary>
/// 容量唤醒选项 - 自动伸缩配置
/// </summary>
[Register(typeof(CapacityWakeOptions), ServiceLifetime.Singleton)]
public sealed partial class CapacityWakeOptions {
    /// <summary>默认最小实例数</summary>
    public const int DefaultMinInstances = 1;

    /// <summary>默认最大实例数</summary>
    public const int DefaultMaxInstances = 5;

    /// <summary>默认扩容阈值百分比</summary>
    public const int DefaultScaleUpThresholdPercent = 80;

    /// <summary>默认缩容阈值百分比</summary>
    public const int DefaultScaleDownThresholdPercent = 20;

    /// <summary>默认检查间隔（毫秒）</summary>
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

    /// <summary>默认构造函数</summary>
    public CapacityWakeOptions() { }

    /// <summary>
    /// 从 Bridge 配置构造容量唤醒选项
    /// </summary>
    /// <param name="config">Bridge 配置</param>
    public CapacityWakeOptions(BridgeConfig config) {
        MinInstances = config.CapacityMinInstances;
        MaxInstances = config.CapacityMaxInstances;
        ScaleUpThreshold = (int)config.CapacityScaleUpThreshold;
        ScaleDownThreshold = (int)config.CapacityScaleDownThreshold;
    }
}

/// <summary>
/// 负载指标 - 当前系统负载快照
/// </summary>
public sealed partial class LoadMetrics {
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
public sealed partial class CapacityChangedEventArgs : EventArgs {
    /// <summary>变更前的实例数</summary>
    public int OldInstanceCount { get; }

    /// <summary>变更后的实例数</summary>
    public int NewInstanceCount { get; }

    /// <summary>触发变更的负载指标快照</summary>
    public LoadMetrics LoadMetrics { get; }

    /// <summary>
    /// 构造容量变更事件参数
    /// </summary>
    /// <param name="oldInstanceCount">变更前的实例数</param>
    /// <param name="newInstanceCount">变更后的实例数</param>
    /// <param name="loadMetrics">触发变更的负载指标</param>
    public CapacityChangedEventArgs(int oldInstanceCount, int newInstanceCount, LoadMetrics loadMetrics) {
        OldInstanceCount = oldInstanceCount;
        NewInstanceCount = newInstanceCount;
        LoadMetrics = loadMetrics;
    }
}

/// <summary>
/// 容量唤醒服务 - 监控负载并自动伸缩实例数
/// 基于负载指标自动扩容/缩容，确保系统在合理容量范围内运行
/// </summary>
[Register(typeof(CapacityWakeService), ServiceLifetime.Singleton)]
public sealed partial class CapacityWakeService : IAsyncDisposable {
    private readonly CapacityWakeOptions _options;
    private readonly ILogger<CapacityWakeService>? _logger;
    private readonly AsyncLock _stateLock = new();

    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private int _currentInstanceCount;
    private LoadMetrics _currentMetrics;
    private int _isDisposed;

    /// <summary>当前实例数</summary>
    public int CurrentInstanceCount => Volatile.Read(ref _currentInstanceCount);

    /// <summary>容量变更事件</summary>
    public event EventHandler<CapacityChangedEventArgs>? CapacityChanged;

    /// <summary>
    /// 构造容量唤醒服务
    /// </summary>
    /// <param name="options">容量唤醒选项</param>
    /// <param name="logger">日志记录器</param>
    public CapacityWakeService(
        CapacityWakeOptions? options = null,
        ILogger<CapacityWakeService>? logger = null) {
        _options = options ?? new CapacityWakeOptions();
        _logger = logger;

        _currentInstanceCount = _options.MinInstances;
        _currentMetrics = new LoadMetrics();
    }

    /// <summary>
    /// 启动监控循环
    /// </summary>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default) {
        using var guard = await _stateLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_stateLock.Name}' 等待超时");

        if (_monitorTask is { IsCompleted: false }) {
            _logger?.LogWarning("[CapacityWake] 监控已在运行");
            return;
        }

        _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitorTask = RunMonitorLoopAsync(_monitorCts.Token);
        _logger?.LogInformation("[CapacityWake] 监控已启动，当前实例数: {InstanceCount}", _currentInstanceCount);

    }

    /// <summary>
    /// 停止监控循环
    /// </summary>
    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default) {
        using var guard = await _stateLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_stateLock.Name}' 等待超时");

        await (_monitorCts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);


        if (_monitorTask is not null) {
            try {
                await _monitorTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) {
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
    public void UpdateLoadMetrics(LoadMetrics metrics) {
        ArgumentNullException.ThrowIfNull(metrics);
        Volatile.Write(ref _currentMetrics, metrics);
    }

    /// <summary>
    /// 手动扩容一个实例
    /// </summary>
    public async Task ScaleUpAsync(CancellationToken cancellationToken = default) {
        using var guard = await _stateLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_stateLock.Name}' 等待超时");

        var oldCount = _currentInstanceCount;
        if (oldCount >= _options.MaxInstances) {
            _logger?.LogWarning("[CapacityWake] 已达最大实例数 {MaxInstances}，无法扩容", _options.MaxInstances);
            return;
        }

        _currentInstanceCount = oldCount + 1;
        _logger?.LogInformation("[CapacityWake] 扩容: {OldCount} -> {NewCount}", oldCount, _currentInstanceCount);
        CapacityChanged?.Invoke(this, new CapacityChangedEventArgs(oldCount, _currentInstanceCount, _currentMetrics));

    }

    /// <summary>
    /// 手动缩容一个实例
    /// </summary>
    public async Task ScaleDownAsync(CancellationToken cancellationToken = default) {
        using var guard = await _stateLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_stateLock.Name}' 等待超时");

        var oldCount = _currentInstanceCount;
        if (oldCount <= _options.MinInstances) {
            _logger?.LogWarning("[CapacityWake] 已达最小实例数 {MinInstances}，无法缩容", _options.MinInstances);
            return;
        }

        _currentInstanceCount = oldCount - 1;
        _logger?.LogInformation("[CapacityWake] 缩容: {OldCount} -> {NewCount}", oldCount, _currentInstanceCount);
        CapacityChanged?.Invoke(this, new CapacityChangedEventArgs(oldCount, _currentInstanceCount, _currentMetrics));

    }

    private readonly CapacityWakeSignal _wakeSignal = new();

    /// <summary>
    /// 唤醒 at-capacity 睡眠 — 对齐 TS 端 capacityWake.wake()
    /// </summary>
    public void WakeUp() => _wakeSignal.WakeUp();

    /// <summary>
    /// 在 at-capacity 时等待唤醒 — 对齐 TS 端 capacityWake.signal() + sleepUntilCapacityWakes()
    /// </summary>
    public Task<bool> SleepUntilCapacityWakesAsync(TimeSpan timeout, CancellationToken ct = default) =>
        _wakeSignal.SleepUntilCapacityWakesAsync(timeout, ct);

    /// <summary>
    /// 监控循环 - 定期检查负载并自动伸缩
    /// </summary>
    private async Task RunMonitorLoopAsync(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                await Task.Delay(_options.CheckIntervalMs, cancellationToken).ConfigureAwait(false);

                var metrics = Volatile.Read(ref _currentMetrics);
                var load = metrics.CompositeLoadPercent;

                if (load >= _options.ScaleUpThreshold) {
                    await ScaleUpAsync(cancellationToken).ConfigureAwait(false);
                } else if (load <= _options.ScaleDownThreshold) {
                    await ScaleDownAsync(cancellationToken).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                _logger?.LogError(ex, "[CapacityWake] 监控循环错误");
            }
        }

        _logger?.LogDebug("[CapacityWake] 监控循环已退出");
    }

    /// <summary>
    /// 异步释放资源 — 停止监控并释放锁与唤醒信号
    /// </summary>
    /// <returns>表示异步释放操作的任务</returns>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) {
            return;
        }

        await StopMonitoringAsync(CancellationToken.None).ConfigureAwait(false);
        _stateLock.Dispose();
        _wakeSignal.Dispose();
    }
}