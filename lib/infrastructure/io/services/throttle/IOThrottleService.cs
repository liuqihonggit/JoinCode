namespace IO.Services;

/// <summary>
/// IO 限流服务实现 - 全局单例
/// 结合 SemaphoreSlim（并发限制）和 Token Bucket（速率限制）
/// </summary>
public sealed partial class IOThrottleService : IIOThrottleService, IDisposable {
    private readonly IOThrottleOptions _options;
    private readonly ILogger<IOThrottleService>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;

    private readonly AsyncLock _readSemaphore;
    private readonly AsyncLock _writeSemaphore;
    private readonly AsyncLock _deleteSemaphore;

    private readonly TokenBucket _tokenBucket;
    private bool _disposed;

    private int _currentConcurrentOperations;

    /// <summary>
    /// 构造 IO 限流服务
    /// </summary>
    /// <param name="options">IO 限流配置选项</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="telemetryService">遥测服务</param>
    /// <param name="clock">时钟服务，默认使用系统时钟</param>
    public IOThrottleService(
        IOptions<IOThrottleOptions> options,
        ILogger<IOThrottleService>? logger = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null) {
        _options = options.Value;
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;

        var validationError = _options.Validate();
        if (validationError != null) {
            throw new InvalidOperationException($"[INF023] IOThrottleOptions 验证失败: {validationError}");
        }

        _readSemaphore = new AsyncLock(nameof(IOThrottleService) + ".Read", _options.MaxConcurrentReads, _options.MaxConcurrentReads);
        _writeSemaphore = new AsyncLock(nameof(IOThrottleService) + ".Write", _options.MaxConcurrentWrites, _options.MaxConcurrentWrites);
        _deleteSemaphore = new AsyncLock(nameof(IOThrottleService) + ".Delete", _options.MaxConcurrentDeletes, _options.MaxConcurrentDeletes);

        _tokenBucket = new TokenBucket(_options.TokenBucketCapacity, _options.TokenRefillRatePerSecond, () => _clock.GetUtcNow());

        _logger?.LogInformation(
            "IOThrottleService initialized: MaxConcurrentReads={MaxReads}, MaxConcurrentWrites={MaxWrites}, " +
            "TokenCapacity={Capacity}, RefillRate={Rate}/s",
            _options.MaxConcurrentReads,
            _options.MaxConcurrentWrites,
            _options.TokenBucketCapacity,
            _options.TokenRefillRatePerSecond);
    }

    /// <summary>
    /// 获取当前并发执行中的 IO 操作数
    /// </summary>
    public int CurrentConcurrentOperations => Interlocked.CompareExchange(ref _currentConcurrentOperations, 0, 0);

    /// <summary>
    /// 获取令牌桶当前可用令牌数
    /// </summary>
    public double CurrentTokens => _tokenBucket.CurrentTokens;

    /// <summary>
    /// 异步获取 IO 执行许可，等待令牌桶和并发槽可用
    /// </summary>
    /// <param name="operationType">IO 操作类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>IO 执行许可，使用完毕需 Dispose</returns>
    public async Task<IIOExecutionLease> AcquireAsync(
        IOOperationType operationType = IOOperationType.Read,
        CancellationToken cancellationToken = default) {
        var lockObj = GetLock(operationType);
        var tokenCost = _options.GetTokenCost(operationType);

        _logger?.LogDebug(
            "Acquiring IO lease for {OperationType}...",
            operationType);

        var stopwatch = Stopwatch.StartNew();

        var releaser = await lockObj.TryLockAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new System.TimeoutException($"锁 '{lockObj.Name}' 等待超时");

        try {
            await _tokenBucket.WaitForTokensAsync(tokenCost, cancellationToken).ConfigureAwait(false);

            Interlocked.Increment(ref _currentConcurrentOperations);

            stopwatch.Stop();

            RecordAcquireMetrics(operationType, stopwatch.ElapsedMilliseconds, true);

            _logger?.LogDebug(
                "IO lease acquired for {OperationType} in {ElapsedMs}ms",
                operationType,
                stopwatch.ElapsedMilliseconds);

            return new IOExecutionLease(this, operationType, _clock, releaser);
        } catch {
            RecordAcquireMetrics(operationType, stopwatch.ElapsedMilliseconds, false);
            releaser.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 尝试同步获取 IO 执行许可，不阻塞等待
    /// </summary>
    /// <param name="operationType">IO 操作类型</param>
    /// <param name="lease">获取成功时输出执行许可，失败时输出 null</param>
    /// <returns>获取成功返回 true，否则返回 false</returns>
    public bool TryAcquire(
        IOOperationType operationType,
        out IIOExecutionLease? lease) {
        var lockObj = GetLock(operationType);
        var tokenCost = _options.GetTokenCost(operationType);

        var releaser = lockObj.TryLock(TimeSpan.Zero, default);
        if (releaser is null) {
            RecordAcquireMetrics(operationType, 0, false);
            lease = null;
            return false;
        }

        try {
            if (!_tokenBucket.TryConsume(tokenCost)) {
                releaser.Dispose();
                RecordAcquireMetrics(operationType, 0, false);
                lease = null;
                return false;
            }

            Interlocked.Increment(ref _currentConcurrentOperations);
            RecordAcquireMetrics(operationType, 0, true);
            lease = new IOExecutionLease(this, operationType, _clock, releaser);
            return true;
        } catch {
            releaser.Dispose();
            RecordAcquireMetrics(operationType, 0, false);
            lease = null;
            return false;
        }
    }

    /// <summary>
    /// 释放指定操作类型的执行许可，递减当前并发计数
    /// </summary>
    /// <param name="operationType">IO 操作类型</param>
    internal void Release(IOOperationType operationType) {
        Interlocked.Decrement(ref _currentConcurrentOperations);

        _logger?.LogDebug(
            "IO lease released for {OperationType}",
            operationType);
    }

    private AsyncLock GetLock(IOOperationType operationType) => operationType switch {
        IOOperationType.Read => _readSemaphore,
        IOOperationType.Write => _writeSemaphore,
        IOOperationType.Delete => _deleteSemaphore,
        _ => throw new ArgumentOutOfRangeException(nameof(operationType))
    };

    private void RecordAcquireMetrics(IOOperationType operationType, long elapsedMs, bool isSuccess) {
        ToolTelemetryHelper.RecordToolCount(_telemetryService, "io.throttle.acquire.count", operationType.ToString(), isSuccess, "IO throttle acquire count");
        if (isSuccess)
            _telemetryService?.RecordHistogram("io.throttle.acquire.duration", elapsedMs, new Dictionary<string, string> { ["operation"] = operationType.ToString() }, "ms", "IO throttle acquire wait duration");
    }

    /// <summary>
    /// 释放限流服务持有的所有资源
    /// </summary>
    public void Dispose() {
        if (_disposed) return; _disposed = true;
        _readSemaphore.Dispose();
        _writeSemaphore.Dispose();
        _deleteSemaphore.Dispose();
        _tokenBucket.Dispose();
    }
}

/// <summary>
/// IO 执行许可实现
/// </summary>
internal sealed class IOExecutionLease : IIOExecutionLease {
    private readonly IOThrottleService _service;
    private readonly IDisposable? _releaser;
    private int _disposed;

    /// <summary>获取许可的时间戳</summary>
    public DateTime AcquiredAt { get; }
    /// <summary>获取许可对应的 IO 操作类型</summary>
    public IOOperationType OperationType { get; }

    /// <summary>
    /// 构造 IO 执行许可
    /// </summary>
    /// <param name="service">所属限流服务</param>
    /// <param name="operationType">IO 操作类型</param>
    /// <param name="clock">时钟服务</param>
    /// <param name="releaser">并发锁释放器</param>
    public IOExecutionLease(IOThrottleService service, IOOperationType operationType, IClockService clock, IDisposable? releaser = null) {
        _service = service;
        OperationType = operationType;
        AcquiredAt = clock.GetUtcNow();
        _releaser = releaser;
    }

    /// <summary>
    /// 释放许可，归还并发槽
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _releaser?.Dispose();
        _service.Release(OperationType);
    }

    /// <summary>
    /// 异步释放许可，归还并发槽
    /// </summary>
    public ValueTask DisposeAsync() {
        Dispose();
        return ValueTask.CompletedTask;
    }
}