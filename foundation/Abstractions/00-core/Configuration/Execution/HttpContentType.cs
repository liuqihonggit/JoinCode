namespace JoinCode.Abstractions.Configuration.Execution;

/// <summary>
/// HTTP Content-Type 枚举 — 替代原 WorkflowConstants.ContentType 静态常量类
/// </summary>
public enum HttpContentType
{
    [EnumValue("application/json")] ApplicationJson,
    [EnumValue("text/event-stream")] TextEventStream,
    [EnumValue("text/plain")] TextPlain,
    [EnumValue("application/octet-stream")] ApplicationOctetStream,
}
