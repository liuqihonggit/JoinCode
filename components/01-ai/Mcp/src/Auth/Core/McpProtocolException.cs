
namespace McpClient;

/// <summary>
/// MCP 协议异常
/// </summary>
public sealed class McpProtocolException : WorkflowException
{
    /// <summary>
    /// JSON-RPC 错误码
    /// </summary>
    public int? JsonRpcErrorCode { get; }

    /// <summary>
    /// 请求ID
    /// </summary>
    public string? RequestId { get; }

    /// <summary>
    /// 方法名
    /// </summary>
    public string? MethodName { get; }

    /// <summary>
    /// 原始消息内容
    /// </summary>
    public string? RawMessage { get; }

    /// <inheritdoc />
    public override bool IsRetryable => JsonRpcErrorCode is -32000 or -32001 or -32603;

    /// <inheritdoc />
    public override int? SuggestedRetryCount => IsRetryable ? 3 : null;

    /// <summary>
    /// 创建 McpProtocolException
    /// </summary>
    public McpProtocolException(
        string message,
        int? jsonRpcErrorCode = null,
        string? requestId = null,
        string? methodName = null,
        string? rawMessage = null,
        ExceptionContext? context = null)
        : base(message, global::JoinCode.Abstractions.Exceptions.ErrorCode.McpProtocol.ToValue(), ErrorCategory.Mcp, context)
    {
        JsonRpcErrorCode = jsonRpcErrorCode;
        RequestId = requestId;
        MethodName = methodName;
        RawMessage = rawMessage;
    }

    /// <summary>
    /// 创建 McpProtocolException（带内部异常）
    /// </summary>
    public McpProtocolException(
        string message,
        Exception innerException,
        int? jsonRpcErrorCode = null,
        string? requestId = null,
        string? methodName = null,
        string? rawMessage = null,
        ExceptionContext? context = null)
        : base(message, innerException, global::JoinCode.Abstractions.Exceptions.ErrorCode.McpProtocol.ToValue(), ErrorCategory.Mcp, context)
    {
        JsonRpcErrorCode = jsonRpcErrorCode;
        RequestId = requestId;
        MethodName = methodName;
        RawMessage = rawMessage;
    }

    /// <summary>
    /// 创建消息解析异常
    /// </summary>
    public static McpProtocolException ParseError(string rawMessage, Exception innerException)
    {
        return new McpProtocolException(
            $"无法解析 JSON-RPC 消息: {innerException.Message}",
            innerException,
            jsonRpcErrorCode: -32700,
            rawMessage: rawMessage);
    }

    /// <summary>
    /// 创建无效请求异常
    /// </summary>
    public static McpProtocolException InvalidRequest(string requestId, string reason)
    {
        return new McpProtocolException(
            $"无效请求: {reason}",
            jsonRpcErrorCode: -32600,
            requestId: requestId);
    }

    /// <summary>
    /// 创建方法未找到异常
    /// </summary>
    public static McpProtocolException MethodNotFound(string methodName, string requestId)
    {
        return new McpProtocolException(
            $"方法未找到: {methodName}",
            jsonRpcErrorCode: -32601,
            requestId: requestId,
            methodName: methodName);
    }

    /// <summary>
    /// 创建无效参数异常
    /// </summary>
    public static McpProtocolException InvalidParams(string methodName, string requestId, string reason)
    {
        return new McpProtocolException(
            $"无效参数: {reason}",
            jsonRpcErrorCode: -32602,
            requestId: requestId,
            methodName: methodName);
    }

    /// <summary>
    /// 创建服务器错误异常
    /// </summary>
    public static McpProtocolException ServerError(string requestId, string reason, int errorCode = -32000)
    {
        return new McpProtocolException(
            $"服务器错误: {reason}",
            jsonRpcErrorCode: errorCode,
            requestId: requestId);
    }
}
