namespace JoinCode.Transport.Bridge;

/// <summary>
/// 传输层致命错误 — 不可恢复的错误（如 epoch 冲突、认证失败）
/// </summary>
public sealed class TransportFatalError : Exception
{
    public int? StatusCode { get; init; }
    public string? ErrorType { get; init; }

    public TransportFatalError(string message, int? statusCode = null, string? errorType = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
    }
}
