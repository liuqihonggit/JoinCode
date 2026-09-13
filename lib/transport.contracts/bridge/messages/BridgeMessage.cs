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
/// 参考 TS 原版 的 SDKMessage 类型
/// </summary>
public abstract class BridgeMessage
{
    /// <summary>消息唯一标识</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>消息类型字符串</summary>
    [JsonPropertyName("type")]
    public abstract string Type { get; }

    /// <summary>消息时间戳（Unix 毫秒）</summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>消息元数据字典</summary>
    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, JsonElement> Metadata { get; init; } = [];
}

/// <summary>
/// SDK 控制请求（来自 IDE 的控制命令）
/// </summary>
public class ControlRequest : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "control_request";

    /// <summary>控制命令名称</summary>
    [JsonPropertyName("command")]
    public string Command { get; init; } = string.Empty;

    /// <summary>命令参数字典</summary>
    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, JsonElement> Params { get; init; } = [];

    /// <summary>获取命令参数字典</summary>
    public Dictionary<string, JsonElement> GetParams()
    {
        return Params;
    }
}

/// <summary>
/// SDK 控制响应
/// </summary>
public class ControlResponse : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "control_response";

    /// <summary>是否成功</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>结果数据</summary>
    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; init; }

    /// <summary>错误描述</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    /// <summary>对应请求的 ID</summary>
    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = string.Empty;
}

/// <summary>
/// 初始化请求
/// </summary>
public class InitializeRequest : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "initialize";

    /// <summary>协议版本</summary>
    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; init; } = "1.0";

    /// <summary>客户端信息</summary>
    [JsonPropertyName("client_info")]
    public ClientInfo ClientInfo { get; init; } = new();

    /// <summary>客户端能力声明</summary>
    [JsonPropertyName("capabilities")]
    public ClientCapabilities Capabilities { get; init; } = new();
}

/// <summary>
/// 初始化响应
/// </summary>
public class InitializeResponse : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "initialize_response";

    /// <summary>协议版本</summary>
    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; init; } = "1.0";

    /// <summary>服务端信息</summary>
    [JsonPropertyName("server_info")]
    public ServerInfo ServerInfo { get; init; } = new();

    /// <summary>服务端能力声明</summary>
    [JsonPropertyName("capabilities")]
    public ServerCapabilities Capabilities { get; init; } = new();
}

/// <summary>
/// 工具列表请求
/// </summary>
public class ToolsListRequest : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "tools/list";
}

/// <summary>
/// 工具列表响应
/// </summary>
public class ToolsListResponse : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "tools/list_response";

    /// <summary>工具定义列表</summary>
    [JsonPropertyName("tools")]
    public List<BridgeToolDefinition> Tools { get; init; } = new();
}

/// <summary>
/// 工具调用请求
/// </summary>
public class ToolsCallRequest : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "tools/call";

    /// <summary>工具名称</summary>
    [JsonPropertyName("tool_name")]
    public string ToolName { get; init; } = string.Empty;

    /// <summary>工具调用参数字典</summary>
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, JsonElement> Arguments { get; init; } = [];

    /// <summary>获取工具调用参数字典</summary>
    public Dictionary<string, JsonElement> GetArguments()
    {
        return Arguments;
    }
}

/// <summary>
/// 工具调用响应
/// </summary>
public class ToolsCallResponse : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "tools/call_response";

    /// <summary>是否成功</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>结果数据</summary>
    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; init; }

    /// <summary>错误描述</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    /// <summary>对应工具调用的 ID</summary>
    [JsonPropertyName("tool_call_id")]
    public string ToolCallId { get; init; } = string.Empty;
}

/// <summary>
/// 技能执行请求
/// </summary>
public class SkillExecuteRequest : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "skill/execute";

    /// <summary>技能名称</summary>
    [JsonPropertyName("skill_name")]
    public string SkillName { get; init; } = string.Empty;

    /// <summary>技能执行参数字典</summary>
    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, JsonElement> Parameters { get; init; } = [];

    /// <summary>技能执行上下文</summary>
    [JsonPropertyName("context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillContext? Context { get; init; }

    /// <summary>获取技能执行参数字典</summary>
    public Dictionary<string, JsonElement> GetParameters()
    {
        return Parameters;
    }
}

/// <summary>
/// 技能执行响应
/// </summary>
public class SkillExecuteResponse : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "skill/execute_response";

    /// <summary>是否成功</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>结果数据</summary>
    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; init; }

    /// <summary>错误描述</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    /// <summary>执行耗时（毫秒）</summary>
    [JsonPropertyName("execution_time_ms")]
    public long ExecutionTimeMs { get; init; }
}

/// <summary>
/// 心跳消息
/// </summary>
public class PingMessage : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "ping";
}

/// <summary>
/// 心跳响应
/// </summary>
public class PongMessage : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "pong";
}

/// <summary>
/// 错误消息
/// </summary>
public class ErrorMessage : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "error";

    /// <summary>错误码</summary>
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>错误消息文本</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>错误详情</summary>
    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Details { get; init; }
}

/// <summary>
/// 通知消息
/// </summary>
public class NotificationMessage : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "notification";

    /// <summary>通知级别（如 info、warning、error）</summary>
    [JsonPropertyName("level")]
    public string Level { get; init; } = "info";

    /// <summary>通知消息文本</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>通知附加数据</summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Data { get; init; }
}

/// <summary>
/// 回显消息（需要过滤）
/// </summary>
public class EchoMessage : BridgeMessage
{
    /// <summary>消息类型字符串</summary>
    public override string Type => "echo";

    /// <summary>原始消息 ID</summary>
    [JsonPropertyName("original_message_id")]
    public string OriginalMessageId { get; init; } = string.Empty;

    /// <summary>回显数据</summary>
    [JsonPropertyName("echo_data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? EchoData { get; init; }
}

#region 辅助模型

/// <summary>
/// 客户端信息
/// </summary>
public class ClientInfo
{
    /// <summary>客户端名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>客户端版本</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
}

/// <summary>
/// 服务端信息
/// </summary>
public class ServerInfo
{
    /// <summary>服务端名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "Core";

    /// <summary>服务端版本</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0.0";
}

/// <summary>
/// 客户端能力声明
/// </summary>
public class ClientCapabilities
{
    /// <summary>工具能力</summary>
    [JsonPropertyName("tools")]
    public ToolCapabilities? Tools { get; init; }

    /// <summary>技能能力</summary>
    [JsonPropertyName("skills")]
    public SkillCapabilities? Skills { get; init; }
}

/// <summary>
/// 服务端能力声明
/// </summary>
public class ServerCapabilities
{
    /// <summary>工具能力</summary>
    [JsonPropertyName("tools")]
    public ToolCapabilities? Tools { get; init; }

    /// <summary>技能能力</summary>
    [JsonPropertyName("skills")]
    public SkillCapabilities? Skills { get; init; }

    /// <summary>协议版本</summary>
    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; init; } = "1.0";
}

/// <summary>
/// 工具能力
/// </summary>
public class ToolCapabilities
{
    /// <summary>是否支持列表变更通知</summary>
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; init; }
}

/// <summary>
/// 技能能力
/// </summary>
public class SkillCapabilities
{
    /// <summary>是否支持列表变更通知</summary>
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; init; }
}

/// <summary>
/// Bridge 工具定义
/// </summary>
public class BridgeToolDefinition
{
    /// <summary>工具名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>工具描述</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>输入参数 Schema</summary>
    [JsonPropertyName("input_schema")]
    public JsonElement InputSchema { get; init; }
}

/// <summary>
/// 技能执行上下文
/// </summary>
public class SkillContext
{
    /// <summary>会话 ID</summary>
    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = string.Empty;

    /// <summary>用户 ID</summary>
    [JsonPropertyName("user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    /// <summary>工作区路径</summary>
    [JsonPropertyName("workspace_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspacePath { get; init; }
}

#endregion
