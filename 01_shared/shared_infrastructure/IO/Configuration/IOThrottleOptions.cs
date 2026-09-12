
namespace IO.Configuration;

/// <summary>
/// IO 限流配置选项
/// </summary>
public sealed class IOThrottleOptions
{
    public const int DefaultMaxConcurrentReads = 10;
    public const int DefaultMaxConcurrentWrites = 5;
    public const int DefaultMaxConcurrentDeletes = 3;
    public const int DefaultTokenBucketCapacity = 100;
    public const double DefaultTokenRefillRatePerSecond = 50;
    public const double DefaultReadTokenCost = 1;
    public const double DefaultWriteTokenCost = 3;
    public const double DefaultDeleteTokenCost = 5;

    /// <summary>
    /// 最大并发读操作数（默认：10）
    /// </summary>
    public int MaxConcurrentReads { get; set; } = DefaultMaxConcurrentReads;

    /// <summary>
    /// 最大并发写操作数（默认：5）
    /// </summary>
    public int MaxConcurrentWrites { get; set; } = DefaultMaxConcurrentWrites;

    /// <summary>
    /// 最大并发删除操作数（默认：3）
    /// </summary>
    public int MaxConcurrentDeletes { get; set; } = DefaultMaxConcurrentDeletes;

    /// <summary>
    /// 令牌桶容量（默认：100）
    /// </summary>
    public int TokenBucketCapacity { get; set; } = DefaultTokenBucketCapacity;

    /// <summary>
    /// 令牌填充速率（每秒令牌数，默认：50）
    /// </summary>
    public double TokenRefillRatePerSecond { get; set; } = DefaultTokenRefillRatePerSecond;

    /// <summary>
    /// 读操作消耗的令牌数（默认：1）
    /// </summary>
    public double ReadTokenCost { get; set; } = DefaultReadTokenCost;

    /// <summary>
    /// 写操作消耗的令牌数（默认：3）
    /// </summary>
    public double WriteTokenCost { get; set; } = DefaultWriteTokenCost;

    /// <summary>
    /// 删除操作消耗的令牌数（默认：5）
    /// </summary>
    public double DeleteTokenCost { get; set; } = DefaultDeleteTokenCost;

    /// <summary>
    /// 验证配置选项的有效性
    /// </summary>
    /// <returns>验证错误消息，如果有效则返回 null</returns>
    public string? Validate()
    {
        return ValidationHelper.CombineErrors(
            ValidatePositive(MaxConcurrentReads, nameof(MaxConcurrentReads)),
            ValidatePositive(MaxConcurrentWrites, nameof(MaxConcurrentWrites)),
            ValidatePositive(MaxConcurrentDeletes, nameof(MaxConcurrentDeletes)),
            ValidatePositive(TokenBucketCapacity, nameof(TokenBucketCapacity)),
            TokenRefillRatePerSecond <= 0 ? $"{nameof(TokenRefillRatePerSecond)} 必须大于 0" : null,
            ReadTokenCost <= 0 ? $"{nameof(ReadTokenCost)} 必须大于 0" : null,
            WriteTokenCost <= 0 ? $"{nameof(WriteTokenCost)} 必须大于 0" : null,
            DeleteTokenCost <= 0 ? $"{nameof(DeleteTokenCost)} 必须大于 0" : null,
            ReadTokenCost > TokenBucketCapacity ? $"{nameof(ReadTokenCost)} 不能超过 {nameof(TokenBucketCapacity)}" : null,
            WriteTokenCost > TokenBucketCapacity ? $"{nameof(WriteTokenCost)} 不能超过 {nameof(TokenBucketCapacity)}" : null,
            DeleteTokenCost > TokenBucketCapacity ? $"{nameof(DeleteTokenCost)} 不能超过 {nameof(TokenBucketCapacity)}" : null
        );
    }

    private static string? ValidatePositive(int value, string fieldName) =>
        value <= 0 ? $"{fieldName} 必须大于 0" : null;

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
