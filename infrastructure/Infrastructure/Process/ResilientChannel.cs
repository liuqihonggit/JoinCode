namespace Infrastructure.Subprocess;

public sealed class ResilientChannel : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly UnifiedCircuitBreaker? _circuitBreaker;
    private readonly string _channelName;
    private readonly TimeSpan _timeout;
    private readonly ILogger? _logger;

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

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        ProbeCircuitBreaker();

        await _lock.WaitAsync(ct).ConfigureAwait(false);
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
        finally
        {
            _lock.Release();
        }
    }

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

    public void Dispose() => _lock.Dispose();
}
