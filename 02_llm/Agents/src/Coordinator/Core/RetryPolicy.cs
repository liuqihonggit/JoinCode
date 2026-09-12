
namespace Core.Agents.Coordinator;

/// <summary>
/// 重试策略配置
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>
    /// 默认重试策略：最多3次，指数退避
    /// </summary>
    public static RetryPolicy Default { get; } = new();

    /// <summary>
    /// 无重试策略
    /// </summary>
    public static RetryPolicy NoRetry { get; } = new() { MaxRetries = 0 };

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// 初始延迟（毫秒）
    /// </summary>
    public int InitialDelayMs { get; init; } = WorkflowConstants.Retry.DefaultRetryDelayMs;

    /// <summary>
    /// 退避乘数
    /// </summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// 最大延迟（毫秒）
    /// </summary>
    public int MaxDelayMs { get; init; } = WorkflowConstants.Retry.MaxDelayMs;

    /// <summary>
    /// 获取第N次重试的延迟时间
    /// </summary>
    public TimeSpan GetDelay(int retryCount)
    {
        if (retryCount <= 0)
        {
            return TimeSpan.Zero;
        }

        var delayMs = InitialDelayMs * Math.Pow(BackoffMultiplier, retryCount - 1);
        var clampedDelayMs = Math.Min(delayMs, MaxDelayMs);
        return TimeSpan.FromMilliseconds(clampedDelayMs);
    }

    /// <summary>
    /// 创建固定延迟策略
    /// </summary>
    public static RetryPolicy FixedDelay(int maxRetries, int delayMs)
    {
        return new RetryPolicy
        {
            MaxRetries = maxRetries,
            InitialDelayMs = delayMs,
            BackoffMultiplier = 1.0
        };
    }

    /// <summary>
    /// 创建指数退避策略
    /// </summary>
    public static RetryPolicy ExponentialBackoff(int maxRetries, int initialDelayMs, double multiplier = 2.0)
    {
        return new RetryPolicy
        {
            MaxRetries = maxRetries,
            InitialDelayMs = initialDelayMs,
            BackoffMultiplier = multiplier
        };
    }
}
