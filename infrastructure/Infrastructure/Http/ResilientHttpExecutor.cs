namespace Infrastructure.Http;

public sealed class ResilientHttpExecutor
{
    private readonly ResiliencePolicy _policy;
    private readonly UnifiedCircuitBreaker? _circuitBreaker;
    private readonly ILogger? _logger;

    public ResilientHttpExecutor(ResiliencePolicy policy, ILogger? logger = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _logger = logger;

        if (policy.CircuitBreaker is not null)
        {
            _circuitBreaker = new UnifiedCircuitBreaker(policy.Name, policy.CircuitBreaker);
        }
    }

    public UnifiedCircuitBreaker? CircuitBreaker => _circuitBreaker;

    /// <summary>
    /// 判断 OperationCanceledException 是否由用户主动取消触发（非连接中断）
    /// TaskCanceledException 继承 OperationCanceledException，但连接中断时 ct.IsCancellationRequested=false
    /// 只有当异常的 CancellationToken 精确匹配用户传入的 ct 时，才是真正的用户取消
    /// </summary>
    private static bool IsUserCancellation(OperationCanceledException ex, CancellationToken ct) =>
        ct.IsCancellationRequested && (ex.CancellationToken == ct || ex.CancellationToken == CancellationToken.None);

    public async Task<HttpResponseMessage> ExecuteAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> operation,
        string operationName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (_circuitBreaker is not null && !_circuitBreaker.TryProbe())
        {
            throw new CircuitBreakerOpenException(
                $"[{_policy.Name}] 熔断器开启: 连续{_circuitBreaker.ConsecutiveFailures}次失败，{_policy.CircuitBreaker!.OpenDuration.TotalSeconds}s 后重试");
        }

        var totalTimeoutCts = _policy.TotalTimeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;

        if (totalTimeoutCts is not null)
        {
            totalTimeoutCts.CancelAfter(_policy.TotalTimeout!.Value);
        }

        var effectiveCt = totalTimeoutCts?.Token ?? ct;

        if (_policy.Retry is null || _policy.Retry.MaxRetries <= 0)
        {
            try
            {
                var response = await ExecuteOnceAsync(operation, operationName, effectiveCt).ConfigureAwait(false);
                _circuitBreaker?.RecordSuccess();
                return response;
            }
            catch (OperationCanceledException ex) when (IsUserCancellation(ex, ct))
            {
                throw;
            }
            catch (Exception)
            {
                _circuitBreaker?.RecordFailure();
                throw;
            }
            finally
            {
                totalTimeoutCts?.Dispose();
            }
        }

        var retry = _policy.Retry;
        var attempt = 0;

        while (true)
        {
            try
            {
                var response = await ExecuteOnceAsync(operation, operationName, effectiveCt).ConfigureAwait(false);
                _circuitBreaker?.RecordSuccess();
                return response;
            }
            catch (OperationCanceledException ex) when (IsUserCancellation(ex, ct))
            {
                throw;
            }
            catch (Exception ex) when (attempt < retry.MaxRetries && ShouldRetry(ex, retry))
            {
                attempt++;
                var delay = CalculateDelay(attempt, retry);

                _logger?.LogWarning(ex,
                    "[{Policy}] {Operation} 失败 (尝试 {Attempt}/{Max}), {Delay}ms 后重试",
                    _policy.Name, operationName, attempt, retry.MaxRetries, delay.TotalMilliseconds);

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _circuitBreaker?.RecordFailure();
                _logger?.LogError(ex, "[{Policy}] {Operation} 最终失败 (尝试 {Attempt})", _policy.Name, operationName, attempt + 1);
                throw;
            }
        }
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (_circuitBreaker is not null && !_circuitBreaker.TryProbe())
        {
            throw new CircuitBreakerOpenException(
                $"[{_policy.Name}] 熔断器开启: 连续{_circuitBreaker.ConsecutiveFailures}次失败");
        }

        var totalTimeoutCts = _policy.TotalTimeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;

        if (totalTimeoutCts is not null)
        {
            totalTimeoutCts.CancelAfter(_policy.TotalTimeout!.Value);
        }

        var effectiveCt = totalTimeoutCts?.Token ?? ct;

        if (_policy.Retry is null || _policy.Retry.MaxRetries <= 0)
        {
            try
            {
                var result = await operation(effectiveCt).ConfigureAwait(false);
                _circuitBreaker?.RecordSuccess();
                return result;
            }
            catch (OperationCanceledException ex) when (IsUserCancellation(ex, ct))
            {
                throw;
            }
            catch (Exception)
            {
                _circuitBreaker?.RecordFailure();
                throw;
            }
            finally
            {
                totalTimeoutCts?.Dispose();
            }
        }

        var retry = _policy.Retry;
        var attempt = 0;

        while (true)
        {
            try
            {
                var result = await operation(effectiveCt).ConfigureAwait(false);
                _circuitBreaker?.RecordSuccess();
                return result;
            }
            catch (OperationCanceledException ex) when (IsUserCancellation(ex, ct))
            {
                throw;
            }
            catch (Exception ex) when (attempt < retry.MaxRetries && ShouldRetry(ex, retry))
            {
                attempt++;
                var delay = CalculateDelay(attempt, retry);

                _logger?.LogWarning(ex,
                    "[{Policy}] {Operation} 失败 (尝试 {Attempt}/{Max}), {Delay}ms 后重试",
                    _policy.Name, operationName, attempt, retry.MaxRetries, delay.TotalMilliseconds);

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _circuitBreaker?.RecordFailure();
                _logger?.LogError(ex, "[{Policy}] {Operation} 最终失败 (尝试 {Attempt})", _policy.Name, operationName, attempt + 1);
                throw;
            }
        }
    }

    private async Task<HttpResponseMessage> ExecuteOnceAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> operation,
        string operationName,
        CancellationToken ct)
    {
        if (_policy.OperationTimeout.HasValue)
        {
            using var opCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            opCts.CancelAfter(_policy.OperationTimeout.Value);

            try
            {
                return await operation(opCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"[INF001] [{_policy.Name}] {operationName} 操作超时 ({_policy.OperationTimeout.Value.TotalSeconds}s)");
            }
        }

        return await operation(ct).ConfigureAwait(false);
    }

    private static bool ShouldRetry(Exception ex, RetryConfig retry)
    {
        if (retry.ShouldRetry is not null)
        {
            return retry.ShouldRetry(ex);
        }

        return ex is TimeoutException
            || ex is TaskCanceledException
            || ex is HttpRequestException
            || ex is IOException;
    }

    private static TimeSpan CalculateDelay(int attempt, RetryConfig retry)
    {
        var delay = retry.Strategy switch
        {
            BackoffStrategy.Fixed => retry.BaseDelay,
            BackoffStrategy.Linear => retry.BaseDelay * attempt,
            BackoffStrategy.Exponential => retry.BaseDelay * Math.Pow(2, attempt - 1),
            BackoffStrategy.ExponentialWithJitter => retry.BaseDelay * Math.Pow(2, attempt - 1),
            _ => retry.BaseDelay
        };

        delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, retry.MaxDelay.TotalMilliseconds));

        if (retry.Strategy == BackoffStrategy.ExponentialWithJitter)
        {
            var jitter = Random.Shared.NextDouble() * 0.5 + 0.75;
            delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * jitter);
        }

        return delay;
    }
}
