namespace Infrastructure.Http;

public sealed class ResilientHttpExecutor
{
    private readonly ResiliencePolicy _policy;
    private readonly UnifiedCircuitBreaker? _circuitBreaker;
    private readonly ILogger? _logger;
    private readonly INetworkConnectivityService? _networkService;

    public ResilientHttpExecutor(ResiliencePolicy policy, ILogger? logger = null, INetworkConnectivityService? networkService = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _logger = logger;
        _networkService = networkService;

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
        var retry = _policy.Retry;
        var hasBudget = retry?.TotalBudget is not null;

        if (retry is null || (retry.MaxRetries <= 0 && !hasBudget))
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

        try
        {
            return await ExecuteRetryLoopAsync(
                ect => ExecuteOnceAsync(operation, operationName, ect),
                operationName, retry!, ct, effectiveCt).ConfigureAwait(false);
        }
        finally
        {
            totalTimeoutCts?.Dispose();
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
        var retry = _policy.Retry;
        var hasBudget = retry?.TotalBudget is not null;

        if (retry is null || (retry.MaxRetries <= 0 && !hasBudget))
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

        try
        {
            return await ExecuteRetryLoopAsync(
                operation, operationName, retry!, ct, effectiveCt).ConfigureAwait(false);
        }
        finally
        {
            totalTimeoutCts?.Dispose();
        }
    }

    /// <summary>
    /// 统一重试循环 — 支持 TotalBudget 预算驱动（24h）和 MaxRetries 驱动（向后兼容）
    /// <para>预算模式：Stopwatch 计时，网络不可用时暂停计时，预算耗尽抛 NetworkRetryBudgetExhaustedException</para>
    /// <para>MaxRetries 模式：重试次数达上限抛最终异常</para>
    /// </summary>
    private async Task<T> ExecuteRetryLoopAsync<T>(
        Func<CancellationToken, Task<T>> executeOnce,
        string operationName,
        RetryConfig retry,
        CancellationToken ct,
        CancellationToken effectiveCt)
    {
        var budget = retry.TotalBudget;
        var sw = budget.HasValue ? Stopwatch.StartNew() : null;
        var attempt = 0;
        var maxLabel = budget.HasValue ? "∞" : retry.MaxRetries.ToString();

        while (true)
        {
            if (sw is not null && sw.Elapsed >= budget!.Value)
            {
                throw new NetworkRetryBudgetExhaustedException(
                    $"[{_policy.Name}] 重试预算耗尽 (尝试 {attempt} 次, 实际 {sw.Elapsed.TotalMilliseconds:F0}ms)");
            }

            try
            {
                var result = await executeOnce(effectiveCt).ConfigureAwait(false);
                _circuitBreaker?.RecordSuccess();
                return result;
            }
            catch (OperationCanceledException ex) when (IsUserCancellation(ex, ct))
            {
                throw;
            }
            catch (Exception ex) when (ShouldRetry(ex, retry) && (budget.HasValue || attempt < retry.MaxRetries))
            {
                attempt++;
                var delay = CalculateDelay(attempt, retry);

                Diag.WriteLine($"[{_policy.Name}:RETRY] {operationName} 失败 (尝试 {attempt}/{maxLabel}), {delay.TotalMilliseconds}ms 后重试 | {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");

                if (retry.PauseBudgetOnNetworkUnavailable && sw is not null
                    && _networkService is not null && !_networkService.IsNetworkAvailable())
                {
                    sw.Stop();
                    await WaitForNetworkAsync(effectiveCt, null).ConfigureAwait(false);
                    sw.Start();
                }
                else
                {
                    await WaitForNetworkAsync(effectiveCt, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _circuitBreaker?.RecordFailure();
                _logger?.LogError("[{Policy}:RETRY] {Operation} 最终失败 (尝试 {Attempt}) | {ExType}: {Message}",
                    _policy.Name, operationName, attempt + 1, ex.GetType().Name, ex.Message);
                throw;
            }
        }
    }

    /// <summary>
    /// 等待网络恢复 — 网络不可用时阻塞等待，恢复后继续
    /// <para>timeout=null：无限等待（预算模式，由 TotalBudget 约束）</para>
    /// <para>timeout=30s：超时后不抛异常，让重试逻辑处理（MaxRetries 模式）</para>
    /// </summary>
    private async Task WaitForNetworkAsync(CancellationToken ct, TimeSpan? timeout = null)
    {
        if (_networkService is null) return;
        if (_networkService.IsNetworkAvailable()) return;

        _logger?.LogWarning("[{Policy}] 网络不可用,等待恢复...", _policy.Name);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<NetworkConnectivityChangedEventArgs> handler = (_, e) =>
        {
            if (e.CurrentState != NetworkConnectivityState.Offline) tcs.TrySetResult(true);
        };
        _networkService.StateChanged += handler;
        try
        {
            if (!_networkService.IsNetworkAvailable())
            {
                var waitTimeout = timeout ?? TimeSpan.MaxValue;
                await tcs.Task.WaitAsync(waitTimeout, ct).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
            _logger?.LogWarning("[{Policy}] 等待网络恢复超时({Timeout}),继续重试",
                _policy.Name, timeout?.TotalSeconds.ToString("F0") + "s" ?? "∞");
        }
        finally
        {
            _networkService.StateChanged -= handler;
        }

        _logger?.LogInformation("[{Policy}] 网络已恢复", _policy.Name);
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
