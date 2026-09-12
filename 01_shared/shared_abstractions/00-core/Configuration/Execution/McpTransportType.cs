namespace JoinCode.Abstractions.Configuration.Execution;

/// <summary>
/// MCP 传输类型枚举 — 替代原 WorkflowConstants.TransportType 静态常量类
/// </summary>
public enum McpTransportType
{
    [EnumValue("stdio")] Stdio,
    [EnumValue("sse")] Sse,
    [EnumValue("http")] Http,
    [EnumValue("websocket")] WebSocket,
}
