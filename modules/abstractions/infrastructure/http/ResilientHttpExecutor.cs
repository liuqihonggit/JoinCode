// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Infrastructure.Http;

/// <summary>
/// 韧性 HTTP 执行器 — 在熔断器、总超时、操作超时和重试策略保护下执行 HTTP 操作
/// </summary>
public sealed class ResilientHttpExecutor {
    private readonly ResiliencePolicy _policy;
    private readonly UnifiedCircuitBreaker? _circuitBreaker;
    private readonly ILogger? _logger;
    private readonly INetworkConnectivityService? _networkService;

    /// <summary>
    /// 构造韧性 HTTP 执行器
    /// </summary>
    /// <param name="policy">韧性策略</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="networkService">网络连通性服务（可选，用于网络中断时暂停重试预算）</param>
    public ResilientHttpExecutor(ResiliencePolicy policy, ILogger? logger = null, INetworkConnectivityService? networkService = null) {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _logger = logger;
        _networkService = networkService;

        if (policy.CircuitBreaker is not null) {
            _circuitBreaker = new UnifiedCircuitBreaker(policy.Name, policy.CircuitBreaker);
        }
    }

    /// <summary>熔断器实例（未配置熔断时为 null）</summary>
    public UnifiedCircuitBreaker? CircuitBreaker => _circuitBreaker;

    /// <summary>
    /// 判断 OperationCanceledException 是否由用户主动取消触发（非连接中断）
    /// TaskCanceledException 继承 OperationCanceledException，但连接中断时 ct.IsCancellationRequested=false
    /// 只有当异常的 CancellationToken 精确匹配用户传入的 ct 时，才是真正的用户取消
    /// </summary>
    private static bool IsUserCancellation(OperationCanceledException ex, CancellationToken ct) =>
        ct.IsCancellationRequested && (ex.CancellationToken == ct || ex.CancellationToken == CancellationToken.None);

    /// <summary>
    /// 执行 HTTP 操作 — 在熔断器、总超时、操作超时和重试策略保护下发送 HTTP 请求
    /// </summary>
    /// <param name="operation">待执行的 HTTP 操作（每次重试都会调用，需自行处理请求克隆）</param>
    /// <param name="operationName">操作名称（用于日志标识）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>HTTP 响应消息</returns>
    public async Task<HttpResponseMessage> ExecuteAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> operation,
        string operationName,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(operation);

        if (_circuitBreaker is not null && _policy.CircuitBreaker is { } cb && !_circuitBreaker.TryProbe()) {
            throw new CircuitBreakerOpenException(
                $"[{_policy.Name}] 熔断器开启: 连续{_circuitBreaker.ConsecutiveFailures}次失败，{cb.OpenDuration.TotalSeconds}s 后重试");
        }

        var totalTimeout = _policy.TotalTimeout;
        using var totalTimeoutCts = totalTimeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;

        if (totalTimeoutCts is not null) {
            totalTimeoutCts.CancelAfter(totalTimeout.GetValueOrDefault());
        }

        var effectiveCt = totalTimeoutCts?.Token ?? ct;
        var retry = _policy.Retry;
        var hasBudget = retry?.TotalBudget is not null;

        if (retry is null || (retry.MaxRetries <= 0 && !hasBudget)) {
            try {
                var response = await ExecuteOnceAsync(operation, operationName, effectiveCt).ConfigureAwait(false);
                _circuitBreaker?.RecordSuccess();
                return response;
            } catch (OperationCanceledException ex) when (IsUserCancellation(ex, ct)) {
                throw;
            } catch (Exception) {
                _circuitBreaker?.RecordFailure();
                throw;
            }
        }

        return await ExecuteRetryLoopAsync(
                ect => ExecuteOnceAsync(operation, operationName, ect),
                operationName, retry ?? throw new InvalidOperationException("retry is null"), ct, effectiveCt).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行泛型操作 — 在熔断器、总超时、操作超时和重试策略保护下执行任意返回类型的操作
    /// </summary>
    /// <typeparam name="T">操作返回类型</typeparam>
    /// <param name="operation">待执行的操作</param>
    /// <param name="operationName">操作名称（用于日志标识）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(operation);

        if (_circuitBreaker is not null && !_circuitBreaker.TryProbe()) {
            throw new CircuitBreakerOpenException(
                $"[{_policy.Name}] 熔断器开启: 连续{_circuitBreaker.ConsecutiveFailures}次失败");
        }

        var totalTimeout = _policy.TotalTimeout;
        using var totalTimeoutCts = totalTimeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;

        if (totalTimeoutCts is not null) {
            totalTimeoutCts.CancelAfter(totalTimeout.GetValueOrDefault());
        }

        var effectiveCt = totalTimeoutCts?.Token ?? ct;
        var retry = _policy.Retry;
        var hasBudget = retry?.TotalBudget is not null;

        if (retry is null || (retry.MaxRetries <= 0 && !hasBudget)) {
            try {
                var result = await operation(effectiveCt).ConfigureAwait(false);
                _circuitBreaker?.RecordSuccess();
                return result;
            } catch (OperationCanceledException ex) when (IsUserCancellation(ex, ct)) {
                throw;
            } catch (Exception) {
                _circuitBreaker?.RecordFailure();
                throw;
            }
        }

        return await ExecuteRetryLoopAsync(
                operation, operationName, retry ?? throw new InvalidOperationException("retry is null"), ct, effectiveCt).ConfigureAwait(false);
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
        CancellationToken effectiveCt) {
        var budget = retry.TotalBudget;
        var sw = budget.HasValue ? Stopwatch.StartNew() : null;
        var attempt = 0;
        var maxLabel = budget.HasValue ? "∞" : retry.MaxRetries.ToString();

        while (true) {
            if (sw is not null && sw.Elapsed >= budget.GetValueOrDefault()) {
                throw new NetworkRetryBudgetExhaustedException(
                    $"[{_policy.Name}] 重试预算耗尽 (尝试 {attempt} 次, 实际 {sw.Elapsed.TotalMilliseconds:F0}ms)");
            }

            try {
                var result = await executeOnce(effectiveCt).ConfigureAwait(false);
                _circuitBreaker?.RecordSuccess();
                return result;
            } catch (OperationCanceledException ex) when (IsUserCancellation(ex, ct)) {
                throw;
            } catch (Exception ex) when (ShouldRetry(ex, retry) && IsRetryAllowed(ex, retry, budget, attempt)) {
                attempt++;
                var delay = CalculateDelay(attempt, retry, ex);

                Diag.WriteLine($"[{_policy.Name}:RETRY] {operationName} 失败 (尝试 {attempt}/{maxLabel}), {delay.TotalMilliseconds}ms 后重试 | {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");

                if (retry.PauseBudgetOnNetworkUnavailable && sw is not null
                    && _networkService is not null && !_networkService.IsNetworkAvailable()) {
                    sw.Stop();
                    await WaitForNetworkAsync(effectiveCt, null).ConfigureAwait(false);
                    sw.Start();
                } else {
                    await WaitForNetworkAsync(effectiveCt, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }

                await DelayWithHeartbeatAsync(delay, retry, ex, attempt, ct).ConfigureAwait(false);
            } catch (Exception ex) {
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
    private async Task WaitForNetworkAsync(CancellationToken ct, TimeSpan? timeout = null) {
        if (_networkService is null) return;
        if (_networkService.IsNetworkAvailable()) return;

        _logger?.LogWarning("[{Policy}] 网络不可用,等待恢复...", _policy.Name);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<NetworkConnectivityChangedEventArgs> handler = (_, e) => {
            if (e.CurrentState != NetworkConnectivityState.Offline) tcs.TrySetResult(true);
        };
        _networkService.StateChanged += handler;
        try {
            if (!_networkService.IsNetworkAvailable()) {
                var waitTimeout = timeout ?? TimeSpan.MaxValue;
                await tcs.Task.WaitAsync(waitTimeout, ct).ConfigureAwait(false);
            }
        } catch (TimeoutException) {
            _logger?.LogWarning("[{Policy}] 等待网络恢复超时({Timeout}),继续重试",
                _policy.Name, timeout?.TotalSeconds.ToString("F0") + "s" ?? "∞");
        } finally {
            _networkService.StateChanged -= handler;
        }

        _logger?.LogInformation("[{Policy}] 网络已恢复", _policy.Name);
    }

    private async Task<HttpResponseMessage> ExecuteOnceAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> operation,
        string operationName,
        CancellationToken ct) {
        if (_policy.OperationTimeout.HasValue) {
            using var opCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            opCts.CancelAfter(_policy.OperationTimeout.Value);

            try {
                return await CheckResponseStatusAsync(await operation(opCts.Token).ConfigureAwait(false), operationName).ConfigureAwait(false);
            } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                throw new TimeoutException($"[INF001] [{_policy.Name}] {operationName} 操作超时 ({_policy.OperationTimeout.Value.TotalSeconds}s)");
            }
        }

        return await CheckResponseStatusAsync(await operation(ct).ConfigureAwait(false), operationName).ConfigureAwait(false);
    }

    /// <summary>
    /// 检查 HTTP 响应状态码 — 非 2xx 抛 HttpRequestException，让重试/熔断逻辑正确处理 LLM API 错误。
    /// </summary>
    private static async Task<HttpResponseMessage> CheckResponseStatusAsync(HttpResponseMessage response, string operationName) {
        if (response.IsSuccessStatusCode) return response;
        var statusCode = (int)response.StatusCode;
        string body;
        try {
            body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        } catch {
            body = "";
        }
        response.Dispose();
        throw new HttpRequestException(
            $"[{operationName}] HTTP {statusCode}: {body.Trim()}",
            null,
            response.StatusCode);
    }

    private static bool ShouldRetry(Exception ex, RetryConfig retry) {
        if (retry.ShouldRetry is not null) {
            return retry.ShouldRetry(ex);
        }

        if (ex is TimeoutException || ex is TaskCanceledException || ex is IOException)
            return true;
        if (ex is HttpRequestException httpEx) {
            var code = (int?)(httpEx.StatusCode);
            if (code is null) return true;
            return code is 408 or 429 or 500 or 502 or 503 or 504;
        }
        return false;
    }

    /// <summary>
    /// 判断是否允许重试 — 区分容量错误（429/529）和其他错误
    /// <para>容量错误 + 预算模式 → 无限重试（对齐基准 persistent 模式仅 429/529 无限）</para>
    /// <para>其他错误 → 有限重试（MaxRetries 驱动）</para>
    /// <para>TransientErrorPredicate 未配置 → 向后兼容（预算模式无限重试所有错误）</para>
    /// </summary>
    private static bool IsRetryAllowed(Exception ex, RetryConfig retry, TimeSpan? budget, int attempt) {
        if (budget is null) return attempt < retry.MaxRetries;
        if (retry.TransientErrorPredicate is null) return true;
        return retry.TransientErrorPredicate(ex) || attempt < retry.MaxRetries;
    }

    private static TimeSpan CalculateDelay(int attempt, RetryConfig retry, Exception? ex = null) {
        var delay = retry.Strategy switch {
            BackoffStrategy.Fixed => retry.BaseDelay,
            BackoffStrategy.Linear => retry.BaseDelay * attempt,
            BackoffStrategy.Exponential => TimeSpan.FromMilliseconds(Math.Min(retry.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1), retry.MaxDelay.TotalMilliseconds)),
            BackoffStrategy.ExponentialWithJitter => TimeSpan.FromMilliseconds(Math.Min(retry.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1), retry.MaxDelay.TotalMilliseconds)),
            _ => retry.BaseDelay
        };

        delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, retry.MaxDelay.TotalMilliseconds));

        if (retry.Strategy == BackoffStrategy.ExponentialWithJitter) {
            var jitter = Random.Shared.NextDouble() * 0.5 + 0.75;
            delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * jitter);
        }

        if (ex is not null && retry.RetryAfterExtractor is { } extractor) {
            var retryAfter = extractor(ex);
            if (retryAfter.HasValue) {
                delay = retryAfter.Value;
            }
        }

        if (retry.SingleDelayCap is { } cap) {
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, cap.TotalMilliseconds));
        }

        return delay;
    }

    /// <summary>
    /// 带心跳的延迟等待 — 将延迟分块为 HeartbeatInterval 片段，每块前调用 HeartbeatCallback
    /// <para>对齐基准 withRetry.ts:489-503 分块睡眠 + yield SystemAPIErrorMessage</para>
    /// <para>未配置心跳时直接整段等待</para>
    /// </summary>
    /// <param name="delay">总等待时间</param>
    /// <param name="retry">重试配置</param>
    /// <param name="ex">当前异常</param>
    /// <param name="attempt">当前尝试次数</param>
    /// <param name="ct">取消令牌</param>
    private static async Task DelayWithHeartbeatAsync(TimeSpan delay, RetryConfig retry, Exception ex, int attempt, CancellationToken ct) {
        if (retry.HeartbeatInterval is not { } interval || retry.HeartbeatCallback is null || delay <= interval) {
            await Task.Delay(delay, ct).ConfigureAwait(false);
            return;
        }

        var remaining = delay;
        while (remaining > TimeSpan.Zero) {
            ct.ThrowIfCancellationRequested();
            retry.HeartbeatCallback(ex, remaining, attempt);
            var chunk = TimeSpan.FromMilliseconds(Math.Min(remaining.TotalMilliseconds, interval.TotalMilliseconds));
            await Task.Delay(chunk, ct).ConfigureAwait(false);
            remaining -= chunk;
        }
    }
}