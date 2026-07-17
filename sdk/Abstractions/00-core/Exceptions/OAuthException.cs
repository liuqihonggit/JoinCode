namespace JoinCode.Abstractions.Exceptions;

public class OAuthException : WorkflowException
{
    public System.Net.HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }

    private const string OAuthErrorCode = "API010";

    public OAuthException(string message)
        : base(message, errorCode: OAuthErrorCode, category: ErrorCategory.Api)
    {
    }

    public OAuthException(string message, System.Net.HttpStatusCode statusCode, string? responseBody)
        : base(message, errorCode: OAuthErrorCode, category: ErrorCategory.Api)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public OAuthException(string message, Exception innerException)
        : base(message, innerException, errorCode: OAuthErrorCode, category: ErrorCategory.Api)
    {
    }
}
