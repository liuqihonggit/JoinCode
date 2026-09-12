namespace Core.Utils;

/// <summary>
/// 网关配置 — 限流/重试/熔断参数。
/// </summary>
/// <param name="MaxConcurrency">最大并发数(默认 10)</param>
/// <param name="MaxRetries">最大重试次数(默认 3)</param>
/// <param name="RetryBaseDelay">重试基础延迟(默认 1s,指数退避:delay * 2^attempt)</param>
/// <param name="CircuitBreakerThreshold">熔断阈值:连续失败次数(0=禁用熔断)</param>
/// <param name="CircuitBreakerRecoveryDelay">熔断恢复延迟(默认 30s)</param>
public sealed record GatewayOptions(
    int MaxConcurrency = 10,
    int MaxRetries = 3,
    TimeSpan RetryBaseDelay = default,
    int CircuitBreakerThreshold = 5,
    TimeSpan CircuitBreakerRecoveryDelay = default)
{
    /// <summary>实际重试基础延迟(默认 1s)</summary>
    public TimeSpan EffectiveRetryDelay => RetryBaseDelay == default ? TimeSpan.FromSeconds(1) : RetryBaseDelay;

    /// <summary>实际熔断恢复延迟(默认 30s)</summary>
    public TimeSpan EffectiveRecoveryDelay => CircuitBreakerRecoveryDelay == default ? TimeSpan.FromSeconds(30) : CircuitBreakerRecoveryDelay;

    /// <summary>LLM Gateway 推荐配置 — 10 并发 + 3 重试 + 5 次熔断</summary>
    public static readonly GatewayOptions LlmGateway = new(
        MaxConcurrency: 10,
        MaxRetries: 3,
        CircuitBreakerThreshold: 5,
        CircuitBreakerRecoveryDelay: default);
}

/// <summary>
/// 网关熔断状态。
/// </summary>
public enum GatewayCircuitState : int
{
    /// <summary>关闭(正常调用)</summary>
    Closed,

    /// <summary>打开(熔断中,拒绝调用)</summary>
    Open,

    /// <summary>半开(允许一次试探调用)</summary>
    HalfOpen
}

/// <summary>
/// 网关事件 — 熔断状态变更事件,通过 OutputAsync 流输出。
/// </summary>
public sealed record GatewayEvent(GatewayCircuitState State, string Message);

/// <summary>
/// 网关 Actor — 通用限流/重试/熔断。
/// <para>继承 ActorBase 获得消息处理 + 输出流,用 LlmGateway 背压配置(容量 200 + 60s 超时)。</para>
/// <para>限流:SemaphoreSlim 控制最大并发。</para>
/// <para>重试:指数退避(RetryBaseDelay * 2^attempt)。</para>
/// <para>熔断:连续失败 CircuitBreakerThreshold 次后打开熔断,RecoveryDelay 后半开试探。</para>
/// <para>熔断事件通过 OutputAsync 流输出。</para>
/// <para>特化用例:LlmGatewayActor = GatewayActor&lt;LlmRequest, LlmResponse&gt;。</para>
/// </summary>
/// <typeparam name="TRequest">请求类型</typeparam>
/// <typeparam name="TResponse">响应类型</typeparam>
public sealed class GatewayActor<TRequest, TResponse> : ActorBase<GatewayActor<TRequest, TResponse>.IGatewayCommand, GatewayEvent>
{
    /// <summary>网关命令标记接口</summary>
    public interface IGatewayCommand;

    private sealed record CallCommand(TRequest Request, TaskCompletionSource<TResponse> Tcs) : IGatewayCommand;

    private readonly SemaphoreSlim _rateLimiter;
    private readonly GatewayOptions _options;
    private readonly Func<TRequest, CancellationToken, Task<TResponse>> _handler;

    private int _breakerState = (int)GatewayCircuitState.Closed;
    private int _consecutiveFailures;
    private DateTimeOffset _breakerOpenedAt = DateTimeOffset.MinValue;

    /// <summary>当前熔断状态 — 用于监控</summary>
    public GatewayCircuitState BreakerState => (GatewayCircuitState)Volatile.Read(ref _breakerState);

    /// <summary>连续失败次数 — 用于监控</summary>
    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    /// <summary>
    /// 构造网关 Actor — 使用 LlmGateway 背压配置。
    /// </summary>
    /// <param name="handler">实际调用函数(如 LLM API 调用)</param>
    /// <param name="options">网关配置(null=LlmGateway 默认)</param>
    /// <param name="backpressure">背压配置(null=LlmGateway 默认:容量 200 + 60s 超时)</param>
    public GatewayActor(
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        GatewayOptions? options = null,
        ActorBackpressure? backpressure = null)
        : base(backpressure ?? ActorBackpressure.LlmGateway)
    {
        _handler = handler;
        _options = options ?? GatewayOptions.LlmGateway;
        _rateLimiter = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
    }

    /// <summary>
    /// 异步调用 — 入队后由 Consumer 串行处理(限流 + 重试 + 熔断)。
    /// </summary>
    public async Task<TResponse> CallAsync(TRequest request, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<TResponse>();
        await SendAsync(new CallCommand(request, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>Consumer 线程内处理调用命令</summary>
    protected override async ValueTask HandleAsync(IGatewayCommand command, CancellationToken ct)
    {
        if (command is CallCommand(var req, var tcs))
        {
            try
            {
                var result = await CallWithRetryAndBreakerAsync(req, ct).ConfigureAwait(false);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }
    }

    private async Task<TResponse> CallWithRetryAndBreakerAsync(TRequest req, CancellationToken ct)
    {
        if (CheckBreakerOpen())
        {
            throw new InvalidOperationException("网关熔断器已打开,拒绝调用");
        }

        await _rateLimiter.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                try
                {
                    var result = await _handler(req, ct).ConfigureAwait(false);
                    OnCallSuccess();
                    return result;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (attempt < _options.MaxRetries)
                    {
                        var delay = _options.EffectiveRetryDelay * Math.Pow(2, attempt);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        continue;
                    }
                    OnCallFailure();
                    throw new InvalidOperationException($"网关调用失败,已重试 {_options.MaxRetries} 次", ex);
                }
            }

            throw new InvalidOperationException("不应到达此处");
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    private bool CheckBreakerOpen()
    {
        if (_options.CircuitBreakerThreshold <= 0) return false;

        var state = (GatewayCircuitState)Volatile.Read(ref _breakerState);
        if (state == GatewayCircuitState.Open)
        {
            var elapsed = DateTimeOffset.UtcNow - _breakerOpenedAt;
            if (elapsed >= _options.EffectiveRecoveryDelay)
            {
                Interlocked.Exchange(ref _breakerState, (int)GatewayCircuitState.HalfOpen);
                return false;
            }
            return true;
        }
        return false;
    }

    private void OnCallSuccess()
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Interlocked.Exchange(ref _breakerState, (int)GatewayCircuitState.Closed);
        TryPublish(new GatewayEvent(GatewayCircuitState.Closed, "调用成功,熔断器关闭"));
    }

    private void OnCallFailure()
    {
        var failures = Interlocked.Increment(ref _consecutiveFailures);
        if (_options.CircuitBreakerThreshold > 0 && failures >= _options.CircuitBreakerThreshold)
        {
            Interlocked.Exchange(ref _breakerState, (int)GatewayCircuitState.Open);
            _breakerOpenedAt = DateTimeOffset.UtcNow;
            TryPublish(new GatewayEvent(GatewayCircuitState.Open, $"连续失败 {failures} 次,熔断器打开"));
        }
    }
}
