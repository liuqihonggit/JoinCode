namespace Core.Utils;

/// <summary>
/// 令牌桶限流器 — 按速率持续补充令牌,消费前需获取足够令牌
/// <para>支持同步 TryConsume 与异步 WaitForTokensAsync 两种消费方式</para>
/// </summary>
public sealed class TokenBucket : IDisposable {
    private readonly double _capacity;
    private readonly double _refillRatePerSecond;
    private readonly Func<DateTime> _timeProvider;
    private BucketState _state;
    private int _disposed;

    private sealed record BucketState(double Tokens, DateTime LastRefillTime);

    /// <summary>当前可用令牌数(读取时触发惰性补充)</summary>
    public double CurrentTokens {
        get {
            while (true) {
                var current = Volatile.Read(ref _state);
                var refilled = Refill(current);
                if (refilled == current)
                    return refilled.Tokens;
                if (Interlocked.CompareExchange(ref _state, refilled, current) == current)
                    return refilled.Tokens;
            }
        }
    }

    /// <summary>
    /// 构造令牌桶
    /// </summary>
    /// <param name="capacity">桶容量,即最大可累积令牌数</param>
    /// <param name="refillRatePerSecond">每秒补充令牌速率</param>
    /// <param name="timeProvider">可选时间提供者,默认使用 DateTime.UtcNow,用于测试注入</param>
    public TokenBucket(double capacity, double refillRatePerSecond, Func<DateTime>? timeProvider = null) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refillRatePerSecond);
        _capacity = capacity;
        _refillRatePerSecond = refillRatePerSecond;
        _timeProvider = timeProvider ?? DefaultTimeProvider;
        _state = new BucketState(capacity, _timeProvider());
    }

    /// <summary>
    /// 异步等待获取指定数量令牌,令牌不足时循环等待直到可用
    /// </summary>
    /// <param name="requiredTokens">需要的令牌数</param>
    /// <param name="ct">取消令牌</param>
    public async Task WaitForTokensAsync(double requiredTokens, CancellationToken ct = default) {
        while (true) {
            var current = Volatile.Read(ref _state);
            var refilled = Refill(current);

            if (refilled.Tokens >= requiredTokens) {
                var newState = refilled with { Tokens = refilled.Tokens - requiredTokens };
                if (Interlocked.CompareExchange(ref _state, newState, current) == current)
                    return;
            }

            await Task.Delay(10, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 尝试同步消费令牌,令牌不足时立即返回 false
    /// </summary>
    /// <param name="requiredTokens">需要的令牌数</param>
    /// <returns>消费成功返回 true,令牌不足返回 false</returns>
    public bool TryConsume(double requiredTokens) {
        while (true) {
            var current = Volatile.Read(ref _state);
            var refilled = Refill(current);

            if (refilled.Tokens >= requiredTokens) {
                var newState = refilled with { Tokens = refilled.Tokens - requiredTokens };
                if (Interlocked.CompareExchange(ref _state, newState, current) == current)
                    return true;
            } else {
                return false;
            }
        }
    }

    private BucketState Refill(BucketState state) {
        var now = _timeProvider();
        var elapsedSeconds = (now - state.LastRefillTime).TotalSeconds;

        if (elapsedSeconds > 0) {
            var tokensToAdd = elapsedSeconds * _refillRatePerSecond;
            return state with { Tokens = Math.Min(_capacity, state.Tokens + tokensToAdd), LastRefillTime = now };
        }
        return state;
    }

    /// <summary>释放令牌桶资源</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
    }

    private static DateTime DefaultTimeProvider() => DateTime.UtcNow;
}
