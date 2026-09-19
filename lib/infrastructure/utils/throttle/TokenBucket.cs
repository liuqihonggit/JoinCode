namespace Core.Utils;

/// <summary>
/// 令牌桶限流器 — 按速率持续补充令牌,消费前需获取足够令牌
/// <para>支持同步 TryConsume 与异步 WaitForTokensAsync 两种消费方式</para>
/// </summary>
public sealed class TokenBucket : IDisposable {
    private readonly AsyncLock _gate = new();
    private readonly double _capacity;
    private readonly double _refillRatePerSecond;
    private readonly Func<DateTime> _timeProvider;
    private double _tokens;
    private DateTime _lastRefillTime;

    /// <summary>当前可用令牌数(读取时触发惰性补充)</summary>
    public double CurrentTokens {
        get {
            var guard = _gate.TryLock();
            if (guard is null)
                return _tokens;

            using (guard) {
                Refill();
                return _tokens;
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
        _tokens = capacity;
        _lastRefillTime = _timeProvider();
    }

    /// <summary>
    /// 异步等待获取指定数量令牌,令牌不足时循环等待直到可用
    /// </summary>
    /// <param name="requiredTokens">需要的令牌数</param>
    /// <param name="ct">取消令牌</param>
    public async Task WaitForTokensAsync(double requiredTokens, CancellationToken ct = default) {
        while (true) {
            using var guard = await _gate.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_gate.Name}' 等待超时");

            Refill();

            if (_tokens >= requiredTokens) {
                _tokens -= requiredTokens;
                return;
            }


            await Task.Delay(10, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 尝试同步消费令牌,令牌不足时立即返回 false
    /// </summary>
    /// <param name="requiredTokens">需要的令牌数</param>
    /// <returns>消费成功返回 true,令牌不足或锁竞争失败返回 false</returns>
    public bool TryConsume(double requiredTokens) {
        var guard = _gate.TryLock();
        if (guard is null)
            return false;

        using (guard) {
            Refill();

            if (_tokens >= requiredTokens) {
                _tokens -= requiredTokens;
                return true;
            }

            return false;
        }
    }

    private void Refill() {
        var now = _timeProvider();
        var elapsedSeconds = (now - _lastRefillTime).TotalSeconds;

        if (elapsedSeconds > 0) {
            var tokensToAdd = elapsedSeconds * _refillRatePerSecond;
            _tokens = Math.Min(_capacity, _tokens + tokensToAdd);
            _lastRefillTime = now;
        }
    }

    /// <summary>释放令牌桶内部锁资源</summary>
    public void Dispose() => _gate.Dispose();

    private static DateTime DefaultTimeProvider() => DateTime.UtcNow;
}