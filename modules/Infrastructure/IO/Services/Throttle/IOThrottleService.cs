
namespace IO.Services;

/// <summary>
/// IO 限流服务实现 - 全局单例
/// 结合 SemaphoreSlim（并发限制）和 Token Bucket（速率限制）
/// </summary>
public sealed partial class IOThrottleService : IIOThrottleService, IDisposable
{
    private readonly IOThrottleOptions _options;
    [Inject] private readonly ILogger<IOThrottleService>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;

    private readonly SemaphoreSlim _readSemaphore;
    private readonly SemaphoreSlim _writeSemaphore;
    private readonly SemaphoreSlim _deleteSemaphore;

    private readonly TokenBucket _tokenBucket;

    private int _currentConcurrentOperations;

    public IOThrottleService(
        IOptions<IOThrottleOptions> options,
        ILogger<IOThrottleService>? logger = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null)
    {
        _options = options.Value;
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;

        var validationError = _options.Validate();
        if (validationError != null)
        {
            throw new InvalidOperationException($"IOThrottleOptions 验证失败: {validationError}");
        }

        _readSemaphore = new SemaphoreSlim(_options.MaxConcurrentReads);
        _writeSemaphore = new SemaphoreSlim(_options.MaxConcurrentWrites);
        _deleteSemaphore = new SemaphoreSlim(_options.MaxConcurrentDeletes);

        _tokenBucket = new TokenBucket(_options.TokenBucketCapacity, _options.TokenRefillRatePerSecond, () => _clock.GetUtcNow());

        _logger?.LogInformation(
            "IOThrottleService initialized: MaxConcurrentReads={MaxReads}, MaxConcurrentWrites={MaxWrites}, " +
            "TokenCapacity={Capacity}, RefillRate={Rate}/s",
            _options.MaxConcurrentReads,
            _options.MaxConcurrentWrites,
            _options.TokenBucketCapacity,
            _options.TokenRefillRatePerSecond);
    }

    public int CurrentConcurrentOperations => Interlocked.CompareExchange(ref _currentConcurrentOperations, 0, 0);

    public double CurrentTokens => _tokenBucket.CurrentTokens;

    public async Task<IIOExecutionLease> AcquireAsync(
        IOOperationType operationType = IOOperationType.Read,
        CancellationToken cancellationToken = default)
    {
        var semaphore = GetSemaphore(operationType);
        var tokenCost = _options.GetTokenCost(operationType);

        _logger?.LogDebug(
            "Acquiring IO lease for {OperationType}...",
            operationType);

        var stopwatch = Stopwatch.StartNew();

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _tokenBucket.WaitForTokensAsync(tokenCost, cancellationToken).ConfigureAwait(false);

            Interlocked.Increment(ref _currentConcurrentOperations);

            stopwatch.Stop();

            RecordAcquireMetrics(operationType, stopwatch.ElapsedMilliseconds, true);

            _logger?.LogDebug(
                "IO lease acquired for {OperationType} in {ElapsedMs}ms",
                operationType,
                stopwatch.ElapsedMilliseconds);

            return new IOExecutionLease(this, operationType, _clock);
        }
        catch
        {
            RecordAcquireMetrics(operationType, stopwatch.ElapsedMilliseconds, false);
            semaphore.Release();
            throw;
        }
    }

    public bool TryAcquire(
        IOOperationType operationType,
        out IIOExecutionLease? lease)
    {
        var semaphore = GetSemaphore(operationType);
        var tokenCost = _options.GetTokenCost(operationType);

        if (!semaphore.Wait(0))
        {
            RecordAcquireMetrics(operationType, 0, false);
            lease = null;
            return false;
        }

        try
        {
            if (!_tokenBucket.TryConsume(tokenCost))
            {
                semaphore.Release();
                RecordAcquireMetrics(operationType, 0, false);
                lease = null;
                return false;
            }

            Interlocked.Increment(ref _currentConcurrentOperations);
            RecordAcquireMetrics(operationType, 0, true);
            lease = new IOExecutionLease(this, operationType, _clock);
            return true;
        }
        catch
        {
            semaphore.Release();
            RecordAcquireMetrics(operationType, 0, false);
            lease = null;
            return false;
        }
    }

    internal void Release(IOOperationType operationType)
    {
        var semaphore = GetSemaphore(operationType);
        semaphore.Release();
        Interlocked.Decrement(ref _currentConcurrentOperations);

        _logger?.LogDebug(
            "IO lease released for {OperationType}",
            operationType);
    }

    private SemaphoreSlim GetSemaphore(IOOperationType operationType) => operationType switch
    {
        IOOperationType.Read => _readSemaphore,
        IOOperationType.Write => _writeSemaphore,
        IOOperationType.Delete => _deleteSemaphore,
        _ => throw new ArgumentOutOfRangeException(nameof(operationType))
    };

    private void RecordAcquireMetrics(IOOperationType operationType, long elapsedMs, bool isSuccess)
    {
        _telemetryService?.RecordCount("io.throttle.acquire.count", new Dictionary<string, string> { ["operation"] = operationType.ToString(), ["success"] = isSuccess.ToString() }, "count", "IO throttle acquire count");
        if (isSuccess)
            _telemetryService?.RecordHistogram("io.throttle.acquire.duration", elapsedMs, new Dictionary<string, string> { ["operation"] = operationType.ToString() }, "ms", "IO throttle acquire wait duration");
    }

    public void Dispose()
    {
        _readSemaphore.Dispose();
        _writeSemaphore.Dispose();
        _deleteSemaphore.Dispose();
        _tokenBucket.Dispose();
    }
}

/// <summary>
/// IO 执行许可实现
/// </summary>
internal sealed class IOExecutionLease : IIOExecutionLease
{
    private readonly IOThrottleService _service;
    private bool _disposed;

    public DateTime AcquiredAt { get; }
    public IOOperationType OperationType { get; }

    public IOExecutionLease(IOThrottleService service, IOOperationType operationType, IClockService clock)
    {
        _service = service;
        OperationType = operationType;
        AcquiredAt = clock.GetUtcNow();
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
        _service.Release(OperationType);
    }
}
