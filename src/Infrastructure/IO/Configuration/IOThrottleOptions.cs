
namespace IO.Configuration;

/// <summary>
/// IO 限流配置选项
/// </summary>
public sealed class IOThrottleOptions
{
    /// <summary>
    /// 最大并发读操作数（默认：10）
    /// </summary>
    public int MaxConcurrentReads { get; set; } = 10;

    /// <summary>
    /// 最大并发写操作数（默认：5）
    /// </summary>
    public int MaxConcurrentWrites { get; set; } = 5;

    /// <summary>
    /// 最大并发删除操作数（默认：3）
    /// </summary>
    public int MaxConcurrentDeletes { get; set; } = 3;

    /// <summary>
    /// 令牌桶容量（默认：100）
    /// </summary>
    public int TokenBucketCapacity { get; set; } = 100;

    /// <summary>
    /// 令牌填充速率（每秒令牌数，默认：50）
    /// </summary>
    public double TokenRefillRatePerSecond { get; set; } = 50;

    /// <summary>
    /// 读操作消耗的令牌数（默认：1）
    /// </summary>
    public double ReadTokenCost { get; set; } = 1;

    /// <summary>
    /// 写操作消耗的令牌数（默认：3）
    /// </summary>
    public double WriteTokenCost { get; set; } = 3;

    /// <summary>
    /// 删除操作消耗的令牌数（默认：5）
    /// </summary>
    public double DeleteTokenCost { get; set; } = 5;

    /// <summary>
    /// 获取操作类型的并发限制
    /// </summary>
    public int GetConcurrencyLimit(IOOperationType operationType) => operationType switch
    {
        IOOperationType.Read => MaxConcurrentReads,
        IOOperationType.Write => MaxConcurrentWrites,
        IOOperationType.Delete => MaxConcurrentDeletes,
        _ => throw new ArgumentOutOfRangeException(nameof(operationType))
    };

    /// <summary>
    /// 获取操作类型的令牌消耗
    /// </summary>
    public double GetTokenCost(IOOperationType operationType) => operationType switch
    {
        IOOperationType.Read => ReadTokenCost,
        IOOperationType.Write => WriteTokenCost,
        IOOperationType.Delete => DeleteTokenCost,
        _ => throw new ArgumentOutOfRangeException(nameof(operationType))
    };
}
