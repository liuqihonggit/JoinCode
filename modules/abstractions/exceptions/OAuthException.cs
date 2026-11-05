namespace JoinCode.Abstractions.Exceptions;

public class OAuthException : WorkflowException {
    /// <summary>获取 HTTP 状态码。</summary>
    public System.Net.HttpStatusCode StatusCode { get; }
    /// <summary>获取响应正文。</summary>
    public string? ResponseBody { get; }

    /// <summary>构造 OAuthException 实例。</summary>
    public OAuthException(string message)
        : base(message, errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiOAuth.ToValue(), category: ErrorCategory.Api) {
    }

    /// <summary>构造 OAuthException 实例并指定状态码与响应正文。</summary>
    public OAuthException(string message, System.Net.HttpStatusCode statusCode, string? responseBody)
        : base(message, errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiOAuth.ToValue(), category: ErrorCategory.Api) {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>构造 OAuthException 实例并指定内部异常。</summary>
    public OAuthException(string message, Exception innerException)
        : base(message, innerException, errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiOAuth.ToValue(), category: ErrorCategory.Api) {
    }
}
