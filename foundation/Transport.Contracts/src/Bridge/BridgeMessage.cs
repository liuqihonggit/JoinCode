namespace JoinCode.Transport.Bridge;

/// <summary>
/// Bridge 消息类型枚举
/// </summary>
public enum BridgeMessageType
{
    /// <summary>初始化请求</summary>
    Initialize,
    /// <summary>初始化响应</summary>
    InitializeResponse,
    /// <summary>工具列表请求</summary>
    ToolsList,
    /// <summary>工具列表响应</summary>
    ToolsListResponse,
    /// <summary>工具调用请求</summary>
    ToolsCall,
    /// <summary>工具调用响应</summary>
    ToolsCallResponse,
    /// <summary>技能执行请求</summary>
    SkillExecute,
    /// <summary>技能执行响应</summary>
    SkillExecuteResponse,
    /// <summary>控制请求</summary>
    ControlRequest,
    /// <summary>控制响应</summary>
    ControlResponse,
    /// <summary>心跳</summary>
    Ping,
    /// <summary>心跳响应</summary>
    Pong,
    /// <summary>错误</summary>
    Error,
    /// <summary>通知</summary>
    Notification,
    /// <summary>回显消息（需要过滤）</summary>
    Echo
}

/// <summary>
/// Bridge 基础消息类
/// 模仿 Claude Code 的 SDKMessage 类型
/// </summary>
public abstract class BridgeMessage
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("type")]
    public abstract string Type { get; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>
/// SDK 控制请求（来自 IDE 的控制命令）
/// </summary>
public class ControlRequest : BridgeMessage
{
    public override string Type => "control_request";

    [JsonPropertyName("command")]
    public string Command { get; init; } = string.Empty;

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Params { get; init; }

    public Dictionary<string, JsonElement> GetParams()
    {
        return Params ?? new Dictionary<string, JsonElement>();
    }
}

/// <summary>
/// SDK 控制响应
/// </summary>
public class ControlResponse : BridgeMessage
{
    public override string Type => "control_response";

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = string.Empty;
}

/// <summary>
/// 初始化请求
/// </summary>
public class InitializeRequest : BridgeMessage
{
    public override string Type => "initialize";

    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; init; } = "1.0";

    [JsonPropertyName("client_info")]
    public ClientInfo ClientInfo { get; init; } = new();

    [JsonPropertyName("capabilities")]
    public ClientCapabilities Capabilities { get; init; } = new();
}

/// <summary>
/// 初始化响应
/// </summary>
public class InitializeResponse : BridgeMessage
{
    public override string Type => "initialize_response";

    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; init; } = "1.0";

    [JsonPropertyName("server_info")]
    public ServerInfo ServerInfo { get; init; } = new();

    [JsonPropertyName("capabilities")]
    public ServerCapabilities Capabilities { get; init; } = new();
}

/// <summary>
/// 工具列表请求
/// </summary>
public class ToolsListRequest : BridgeMessage
{
    public override string Type => "tools/list";
}

/// <summary>
/// 工具列表响应
/// </summary>
public class ToolsListResponse : BridgeMessage
{
    public override string Type => "tools/list_response";

    [JsonPropertyName("tools")]
    public List<BridgeToolDefinition> Tools { get; init; } = new();
}

/// <summary>
/// 工具调用请求
/// </summary>
public class ToolsCallRequest : BridgeMessage
{
    public override string Type => "tools/call";

    [JsonPropertyName("tool_name")]
    public string ToolName { get; init; } = string.Empty;

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Arguments { get; init; }

    public Dictionary<string, JsonElement> GetArguments()
    {
        return Arguments ?? new Dictionary<string, JsonElement>();
    }
}

/// <summary>
/// 工具调用响应
/// </summary>
public class ToolsCallResponse : BridgeMessage
{
    public override string Type => "tools/call_response";

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    [JsonPropertyName("tool_call_id")]
    public string ToolCallId { get; init; } = string.Empty;
}

/// <summary>
/// 技能执行请求
/// </summary>
public class SkillExecuteRequest : BridgeMessage
{
    public override string Type => "skill/execute";

    [JsonPropertyName("skill_name")]
    public string SkillName { get; init; } = string.Empty;

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Parameters { get; init; }

    [JsonPropertyName("context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillContext? Context { get; init; }

    public Dictionary<string, JsonElement> GetParameters()
    {
        return Parameters ?? new Dictionary<string, JsonElement>();
    }
}

/// <summary>
/// 技能执行响应
/// </summary>
public class SkillExecuteResponse : BridgeMessage
{
    public override string Type => "skill/execute_response";

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    [JsonPropertyName("execution_time_ms")]
    public long ExecutionTimeMs { get; init; }
}

/// <summary>
/// 心跳消息
/// </summary>
public class PingMessage : BridgeMessage
{
    public override string Type => "ping";
}

/// <summary>
/// 心跳响应
/// </summary>
public class PongMessage : BridgeMessage
{
    public override string Type => "pong";
}

/// <summary>
/// 错误消息
/// </summary>
public class ErrorMessage : BridgeMessage
{
    public override string Type => "error";

    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Details { get; init; }
}

/// <summary>
/// 通知消息
/// </summary>
public class NotificationMessage : BridgeMessage
{
    public override string Type => "notification";

    [JsonPropertyName("level")]
    public string Level { get; init; } = "info";

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Data { get; init; }
}

/// <summary>
/// 回显消息（需要过滤）
/// </summary>
public class EchoMessage : BridgeMessage
{
    public override string Type => "echo";

    [JsonPropertyName("original_message_id")]
    public string OriginalMessageId { get; init; } = string.Empty;

    [JsonPropertyName("echo_data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? EchoData { get; init; }
}

#region 辅助模型

public class ClientInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
}

public class ServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "Core";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0.0";
}

public class ClientCapabilities
{
    [JsonPropertyName("tools")]
    public ToolCapabilities? Tools { get; init; }

    [JsonPropertyName("skills")]
    public SkillCapabilities? Skills { get; init; }
}

public class ServerCapabilities
{
    [JsonPropertyName("tools")]
    public ToolCapabilities? Tools { get; init; }

    [JsonPropertyName("skills")]
    public SkillCapabilities? Skills { get; init; }

    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; init; } = "1.0";
}

public class ToolCapabilities
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; init; }
}

public class SkillCapabilities
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; init; }
}

public class BridgeToolDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("input_schema")]
    public JsonElement InputSchema { get; init; }
}

public class SkillContext
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    [JsonPropertyName("workspace_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspacePath { get; init; }
}

#endregion
