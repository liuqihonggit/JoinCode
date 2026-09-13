namespace JoinCode.Abstractions.Utils;

/// <summary>
/// MCP 协议常量（协议版本/错误码/JSON-RPC版本已迁移至 McpProtocol.Contracts.JsonRpc/McpProtocolVersion/ErrorCodes）
/// </summary>
public static class McpConstants
{
    /// <summary>
    /// 默认服务器名称
    /// </summary>
    public const string DefaultServerName = "JoinCode.McpServer";

    /// <summary>
    /// 默认服务器版本
    /// </summary>
    public const string DefaultServerVersion = "1.0.0";

    /// <summary>
    /// JSON-RPC 错误码: 服务器错误起始值
    /// </summary>
    public const int ErrorServerErrorStart = -32099;

    /// <summary>
    /// JSON-RPC 错误码: 服务器错误结束值
    /// </summary>
    public const int ErrorServerErrorEnd = -32000;

    /// <summary>
    /// MCP 错误码: URL Elicitation 必需（对齐 TS ErrorCode.UrlElicitationRequired）
    /// </summary>
    public const int ErrorUrlElicitationRequired = -32042;
}
