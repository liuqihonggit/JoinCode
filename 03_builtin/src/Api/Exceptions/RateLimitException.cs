
namespace Services.Api;

/// <summary>
/// 速率限制异常 - 当 API 返回 429 状态码时抛出
/// </summary>
public sealed class RateLimitException : ApiException
{
    /// <summary>
    /// 建议重试等待时间
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// 创建 RateLimitException
    /// </summary>
    public RateLimitException(
        string endpoint,
        TimeSpan? retryAfter = null,
        string? responseContent = null)
        : base(
            $"API 请求被限流: {endpoint}. {(retryAfter.HasValue ? $"请在 {retryAfter.Value.TotalSeconds}s 后重试" : "请稍后重试")}",
            statusCode: 429,
            endpoint: endpoint,
            responseContent: responseContent,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiRateLimit.ToValue())
    {
        RetryAfter = retryAfter;
    }

    /// <inheritdoc />
    public override bool IsRetryable => true;

    /// <inheritdoc />
    public override int? SuggestedRetryCount => 5;
}
