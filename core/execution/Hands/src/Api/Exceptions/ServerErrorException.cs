
namespace Services.Api;

/// <summary>
/// 服务器错误异常 - 当 API 返回 5xx 状态码时抛出
/// </summary>
public sealed class ServerErrorException : ApiException
{
    /// <summary>
    /// 创建 ServerErrorException
    /// </summary>
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
