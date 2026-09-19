namespace McpProtocol.Contracts;

/// <summary>
/// JSON-RPC 2.0 协议常量
/// </summary>
public static class JsonRpc {
    /// <summary>JSON-RPC 协议版本号</summary>
    public const string ProtocolVersion = "2.0";
    /// <summary>LSP 风格 Content-Length 框架协议前缀</summary>
    public const string ContentLengthPrefix = "Content-Length: ";
}

/// <summary>
/// MCP 协议版本常量 — 对齐官方规范版本号
/// </summary>
public static class McpProtocolVersion {
    /// <summary>2024-11-05 规范版本(已归档)</summary>
    public const string V2024_11_05 = "2024-11-05";
    /// <summary>2025-03-26 规范版本</summary>
    public const string V2025_03_26 = "2025-03-26";
    /// <summary>2025-06-18 规范版本</summary>
    public const string V2025_06_18 = "2025-06-18";
    /// <summary>2025-11-25 规范版本(Streamable HTTP)</summary>
    public const string V2025_11_25 = "2025-11-25";

    /// <summary>当前生效的 MCP 协议版本</summary>
    public const string Current = V2025_11_25;

    /// <summary>服务端支持的协议版本集合(按优先级降序排列)</summary>
    public static readonly FrozenSet<string> Supported = FrozenSet.Create(
        StringComparer.Ordinal,
        V2025_11_25, V2025_06_18, V2025_03_26);
}

/// <summary>
/// JSON-RPC 错误码常量 — 对齐 RFC 规范定义
/// </summary>
public static class ErrorCodes {
    /// <summary>解析错误:服务端收到无效 JSON</summary>
    public const int ParseError = -32700;
    /// <summary>无效请求:发送的 JSON 不是有效请求对象</summary>
    public const int InvalidRequest = -32600;
    /// <summary>方法不存在:方法名无效或不可用</summary>
    public const int MethodNotFound = -32601;
    /// <summary>无效参数:方法参数无效</summary>
    public const int InvalidParams = -32602;
    /// <summary>内部错误:服务端内部异常</summary>
    public const int InternalError = -32603;
}

/// <summary>
/// JSON 值类型字符串常量 — 对齐 JSON Schema type 字段取值
/// </summary>
public static class JsonValueTypes {
    /// <summary>字符串类型</summary>
    public const string String = "string";
    /// <summary>整数类型</summary>
    public const string Integer = "integer";
    /// <summary>数值类型(含浮点)</summary>
    public const string Number = "number";
    /// <summary>布尔类型</summary>
    public const string Boolean = "boolean";
    /// <summary>对象类型</summary>
    public const string Object = "object";
    /// <summary>数组类型</summary>
    public const string Array = "array";
}