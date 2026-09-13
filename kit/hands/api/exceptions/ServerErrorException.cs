
namespace Services.Api;

/// <summary>
/// 服务器错误异常 - 当 API 返回 5xx 状态码时抛出
/// </summary>
public sealed class ServerErrorException : ApiException
{
    /// <summary>
    /// 创建 ServerErrorException
    /// </summary>
    /// <param name="endpoint">API 端点</param>
    /// <param name="statusCode">HTTP 状态码</param>
    /// <param name="responseContent">原始响应内容</param>
    public ServerErrorException(
        string endpoint,
        int statusCode,
        string? responseContent = null)
        : base(
            $"API 服务器错误 (HTTP {statusCode}): {endpoint}",
            statusCode: statusCode,
            endpoint: endpoint,
            responseContent: responseContent,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiServerError.ToValue())
    {
    }

    /// <inheritdoc />
    public override bool IsRetryable => true;

    /// <inheritdoc />
    public override int? SuggestedRetryCount => 3;
}
