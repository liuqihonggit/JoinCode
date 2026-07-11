
namespace Services.Api;

/// <summary>
/// 认证错误异常 - 当 API 返回 401/403 状态码时抛出
/// </summary>
public sealed class AuthException : ApiException
{
    /// <summary>
    /// 创建 AuthException
    /// </summary>
    public AuthException(
        string endpoint,
        int statusCode,
        string reason,
        string? responseContent = null)
        : base(
            $"API 认证失败: {reason}",
            statusCode: statusCode,
            endpoint: endpoint,
            responseContent: responseContent,
            errorCode: statusCode == 401 ? global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiAuthentication.ToValue() : global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiAuthorization.ToValue())
    {
    }

    /// <inheritdoc />
    public override bool IsRetryable => false;
}
