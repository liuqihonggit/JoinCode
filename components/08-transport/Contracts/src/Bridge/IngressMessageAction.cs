namespace JoinCode.Transport.Bridge;

/// <summary>
/// 入站消息处理结果
/// </summary>
public enum IngressMessageAction
{
    /// <summary>权限响应（远端客户端回答了权限提示）</summary>
    [EnumValue("permissionResponse")] PermissionResponse,
    /// <summary>服务器控制请求（initialize/set_model/interrupt 等）</summary>
    [EnumValue("controlRequest")] ControlRequest,
    /// <summary>用户消息（需要转发给 REPL）</summary>
    [EnumValue("inboundMessage")] InboundMessage,
    /// <summary>回声消息（已过滤）</summary>
    [EnumValue("echoFiltered")] EchoFiltered,
    /// <summary>重复消息（已过滤）</summary>
    [EnumValue("duplicateFiltered")] DuplicateFiltered,
    /// <summary>非用户消息（已忽略）</summary>
    [EnumValue("nonUserIgnored")] NonUserIgnored,
    /// <summary>解析失败</summary>
    [EnumValue("parseError")] ParseError
}

/// <summary>
/// 入站消息字段 — 对齐 TS 端 extractInboundMessageFields 返回值
/// </summary>
public sealed class InboundMessageFields
{
    /// <summary>文本内容（当 content 是字符串时）</summary>
    public string? Content { get; init; }

    /// <summary>内容块列表（当 content 是数组时）</summary>
    public List<Dictionary<string, JsonElement>>? ContentBlocks { get; init; }

    /// <summary>消息 UUID</summary>
    public string? Uuid { get; init; }
}
