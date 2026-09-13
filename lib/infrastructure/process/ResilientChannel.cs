namespace Infrastructure.Subprocess;

/// <summary>
/// 韧性通道 — 在异步锁和熔断器保护下执行操作，超时或失败时记录熔断器失败，成功时记录成功
/// </summary>
public sealed class ResilientChannel : IDisposable
{
    private readonly AsyncLock _lock = new();
    private readonly UnifiedCircuitBreaker? _circuitBreaker;
    private readonly string _channelName;
    private readonly TimeSpan _timeout;
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造韧性通道
    /// </summary>
    /// <param name="channelName">通道名称（用于日志和异常标识）</param>
    /// <param name="circuitBreaker">熔断器（可选，null 表示不启用熔断保护）</param>
    /// <param name="timeout">单次操作超时时长</param>
    /// <param name="logger">日志记录器（可选）</param>
    public ResilientChannel(
        string channelName,
        UnifiedCircuitBreaker? circuitBreaker,
        TimeSpan timeout,
        ILogger? logger = null)
    {
        _channelName = channelName;
        _circuitBreaker = circuitBreaker;
        _timeout = timeout;
        _logger = logger;
    }

    /// <summary>
    /// 在熔断器和锁保护下执行操作 — 超时抛 TimeoutException，失败记录熔断器失败
    /// </summary>
    /// <typeparam name="T">操作返回类型</typeparam>
    /// <param name="operation">待执行的操作</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        ProbeCircuitBreaker();

        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_timeout);

            var result = await operation(timeoutCts.Token).ConfigureAwait(false);

            _circuitBreaker?.RecordSuccess();
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _circuitBreaker?.RecordFailure();
            throw new TimeoutException($"[INF039] [{_channelName}] 操作超时 ({_timeout.TotalSeconds}s)");
        }
        catch (Exception ex)
        {
            _circuitBreaker?.RecordFailure();
            _logger?.LogWarning(ex, "[{ChannelName}] 操作失败", _channelName);
            throw;
        }

    }

    /// <summary>
    /// 在熔断器和锁保护下执行无返回值操作 — 委托给泛型版本
    /// </summary>
    /// <param name="operation">待执行的操作</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
        ExecuteAsync<object?>(async ct =>
        {
            await operation(ct).ConfigureAwait(false);
            return null;
        }, ct);

    private void ProbeCircuitBreaker()
    {
        if (_circuitBreaker is not null && !_circuitBreaker.TryProbe())
        {
            throw new CircuitBreakerOpenException($"[INF040] [{_channelName}] 熔断器开启，停止通讯");
        }
    }

    /// <summary>
    /// 释放通道 — 释放内部异步锁
    /// </summary>
    public void Dispose() => _lock.Dispose();
}
