namespace JoinCode.Transport.Bridge;

/// <summary>
/// 传输层致命错误 — 不可恢复的错误（如 epoch 冲突、认证失败）
/// </summary>
public sealed class TransportFatalError : Exception {
    /// <summary>HTTP 状态码（可选）</summary>
    public int? StatusCode { get; init; }
    /// <summary>错误类型标识（可选）</summary>
    public string? ErrorType { get; init; }

    /// <summary>
    /// 构造传输致命错误
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="statusCode">HTTP 状态码（可选）</param>
    /// <param name="errorType">错误类型标识（可选）</param>
    public TransportFatalError(string message, int? statusCode = null, string? errorType = null)
        : base(message) {
        StatusCode = statusCode;
        ErrorType = errorType;
    }
}