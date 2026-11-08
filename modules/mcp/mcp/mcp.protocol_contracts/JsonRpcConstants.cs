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