namespace JoinCode.Transport;

/// <summary>
/// 重连策略 — 指数退避算法
/// </summary>
/// <remarks>
/// 退避公式: backoffMs = Min(InitialBackoffMs * 2^(attempt-1), MaxBackoffMs)
/// </remarks>
public sealed class ReconnectPolicy
{
    /// <summary>最大重试次数</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>初始退避时间（毫秒）</summary>
    public int InitialBackoffMs { get; init; } = 1000;

    /// <summary>最大退避时间（毫秒）</summary>
    public int MaxBackoffMs { get; init; } = 30000;

    /// <summary>
    /// 根据重试次数等待退避时间
    /// </summary>
    /// <param name="attempt">当前重试次数（从1开始）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>true 表示继续重试，false 表示已达到最大次数</returns>
    public async Task<bool> WaitAsync(int attempt, CancellationToken ct = default)
    {
        if (attempt > MaxAttempts) return false;

        var backoff = new ExponentialBackoff(
            TimeSpan.FromMilliseconds(InitialBackoffMs),
            TimeSpan.FromMilliseconds(MaxBackoffMs));
        var delay = backoff.CalculateDelay(attempt - 1);
        await Task.Delay(delay, ct).ConfigureAwait(false);
        return true;
    }
}
