namespace JoinCode.Abstractions.Exceptions;

/// <summary>
/// API 异常
/// </summary>
public class ApiException : WorkflowException
{
    /// <summary>
    /// HTTP 状态码
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// API 端点
    /// </summary>
    public string? Endpoint { get; }

    /// <summary>
    /// 响应内容
    /// </summary>
    public string? ResponseContent { get; }

    /// <inheritdoc />
    public override bool IsRetryable => StatusCode is >= 500 or 429 or 408 or null;

    /// <inheritdoc />
    public override int? SuggestedRetryCount => IsRetryable ? 3 : null;

    /// <summary>
    /// 创建 ApiException
    /// </summary>
    public ApiException(
        string message,
        int? statusCode = null,
        string? endpoint = null,
        string? responseContent = null,
        string? errorCode = null,
        ExceptionContext? context = null)
        : base(message, errorCode ?? global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiGeneral.ToValue(), ErrorCategory.Api, context)
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        ResponseContent = responseContent;
    }

    /// <summary>
    /// 创建 ApiException（带内部异常）
    /// </summary>
    public ApiException(
        string message,
        Exception? innerException,
        int? statusCode = null,
        string? endpoint = null,
        string? responseContent = null,
        string? errorCode = null,
        ExceptionContext? context = null)
        : base(message, innerException, errorCode ?? global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiGeneral.ToValue(), ErrorCategory.Api, context)
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        ResponseContent = responseContent;
    }

    /// <summary>
    /// 创建连接异常
    /// </summary>
    public static ApiException Connection(string endpoint, Exception? innerException = null)
    {
        return new ApiException(
            $"无法连接到 API 端点: {endpoint}",
            innerException,
            endpoint: endpoint,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiConnection.ToValue());
    }

    /// <summary>
    /// 创建超时异常
    /// </summary>
    public static ApiException Timeout(string endpoint, TimeSpan? timeout = null)
    {
        var timeoutMsg = timeout.HasValue ? $" (超时: {timeout.Value.TotalSeconds}s)" : string.Empty;
        return new ApiException(
            $"API 请求超时{timeoutMsg}: {endpoint}",
            endpoint: endpoint,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiTimeout.ToValue());
    }

    /// <summary>
    /// 创建限流异常
    /// </summary>
    public static ApiException RateLimit(string endpoint, TimeSpan? retryAfter = null)
    {
        var retryMsg = retryAfter.HasValue ? $" 请在 {retryAfter.Value.TotalSeconds}s 后重试" : string.Empty;
        var ex = new ApiException(
            $"API 请求被限流: {endpoint}.{retryMsg}",
            statusCode: 429,
            endpoint: endpoint,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiRateLimit.ToValue());
        return ex;
    }

    /// <summary>
    /// 创建认证异常
    /// </summary>
    public static ApiException Authentication(string endpoint, string reason)
    {
        return new ApiException(
            $"API 认证失败: {reason}",
            statusCode: 401,
            endpoint: endpoint,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiAuthentication.ToValue());
    }

    /// <summary>
    /// 创建响应错误异常
    /// </summary>
    public static ApiException ResponseError(string endpoint, int statusCode, string responseContent)
    {
        return new ApiException(
            $"API 返回错误响应 (HTTP {statusCode})",
            statusCode: statusCode,
            endpoint: endpoint,
            responseContent: responseContent,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiResponseError.ToValue());
    }
}
