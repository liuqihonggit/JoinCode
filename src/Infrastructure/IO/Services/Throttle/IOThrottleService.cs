
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

    // 并发限制 - 按操作类型分别控制
    private readonly SemaphoreSlim _readSemaphore;
    private readonly SemaphoreSlim _writeSemaphore;
    private readonly SemaphoreSlim _deleteSemaphore;

    // 速率限制 - Token Bucket
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private double _tokens;
    private DateTime _lastRefillTime;

    // 统计
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

        // 初始化信号量
        _readSemaphore = new SemaphoreSlim(_options.MaxConcurrentReads);
        _writeSemaphore = new SemaphoreSlim(_options.MaxConcurrentWrites);
        _deleteSemaphore = new SemaphoreSlim(_options.MaxConcurrentDeletes);

        // 初始化令牌桶
        _tokens = _options.TokenBucketCapacity;
        _lastRefillTime = _clock.GetUtcNow();

        _logger?.LogInformation(
            "IOThrottleService initialized: MaxConcurrentReads={MaxReads}, MaxConcurrentWrites={MaxWrites}, " +
            "TokenCapacity={Capacity}, RefillRate={Rate}/s",
            _options.MaxConcurrentReads,
            _options.MaxConcurrentWrites,
            _options.TokenBucketCapacity,
            _options.TokenRefillRatePerSecond);
    }

    public int CurrentConcurrentOperations => Interlocked.CompareExchange(ref _currentConcurrentOperations, 0, 0);

    public double CurrentTokens
    {
        get
        {
            if (!_tokenSemaphore.Wait(0))
            {
                return _tokens;
            }

            try
            {
                RefillTokens();
                return _tokens;
            }
            finally
            {
                _tokenSemaphore.Release();
            }
        }
    }

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

        // 1. 等待并发许可
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 2. 等待速率限制许可
            await WaitForTokensAsync(tokenCost, cancellationToken).ConfigureAwait(false);

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
            // 如果速率限制失败，释放并发许可
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

        // 1. 尝试获取并发许可
        if (!semaphore.Wait(0))
        {
            RecordAcquireMetrics(operationType, 0, false);
            lease = null;
            return false;
        }

        try
        {
            // 2. 尝试获取速率限制许可
            if (!TryConsumeTokens(tokenCost))
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

    private async Task WaitForTokensAsync(double requiredTokens, CancellationToken cancellationToken)
    {
        while (true)
        {
            await _tokenSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                RefillTokens();

                if (_tokens >= requiredTokens)
                {
                    _tokens -= requiredTokens;
                    return;
                }
            }
            finally
            {
                _tokenSemaphore.Release();
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool TryConsumeTokens(double requiredTokens)
    {
        if (!_tokenSemaphore.Wait(0))
        {
            return false;
        }

        try
        {
            RefillTokens();

            if (_tokens >= requiredTokens)
            {
                _tokens -= requiredTokens;
                return true;
            }

            return false;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    private void RefillTokens()
    {
        var now = _clock.GetUtcNow();
        var elapsedSeconds = (now - _lastRefillTime).TotalSeconds;

        if (elapsedSeconds > 0)
        {
            var tokensToAdd = elapsedSeconds * _options.TokenRefillRatePerSecond;
            _tokens = Math.Min(_options.TokenBucketCapacity, _tokens + tokensToAdd);
            _lastRefillTime = now;
        }
    }

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
        _tokenSemaphore.Dispose();
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
        if (!_disposed)
        {
            _service.Release(OperationType);
            _disposed = true;
        }
    }
}
