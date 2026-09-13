namespace JoinCode.Abstractions.Exceptions;

public class OAuthException : WorkflowException
{
    public System.Net.HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }

    public OAuthException(string message)
        : base(message, errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiOAuth.ToValue(), category: ErrorCategory.Api)
    {
    }

    public OAuthException(string message, System.Net.HttpStatusCode statusCode, string? responseBody)
        : base(message, errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiOAuth.ToValue(), category: ErrorCategory.Api)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public OAuthException(string message, Exception innerException)
        : base(message, innerException, errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiOAuth.ToValue(), category: ErrorCategory.Api)
    {
    }
}
