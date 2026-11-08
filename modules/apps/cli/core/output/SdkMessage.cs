namespace JoinCode.Cli.Output;

[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SdkTextBlock), "text")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SdkThinkingBlock), "thinking")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SdkToolUseBlock), "tool_use")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SdkToolResultBlock), "tool_result")]
public abstract record SdkContentBlock;

public sealed record SdkTextBlock : SdkContentBlock {
    /// <summary>文本内容</summary>
    public string Text { get; init; } = string.Empty;
}

public sealed record SdkThinkingBlock : SdkContentBlock {
    /// <summary>思考内容</summary>
    public string Thinking { get; init; } = string.Empty;
}

public sealed record SdkToolUseBlock : SdkContentBlock {
    /// <summary>工具调用标识</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>工具名称</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>工具参数 JSON 对象 — 对齐基准侧 tool_use.input 为 JSON 对象而非字符串</summary>
    public System.Text.Json.JsonElement Input { get; init; }
}

public sealed record SdkToolResultBlock : SdkContentBlock {
    /// <summary>对应的 tool_use id</summary>
    [System.Text.Json.Serialization.JsonPropertyName("tool_use_id")]
    public string ToolUseId { get; init; } = string.Empty;
    /// <summary>工具结果文本</summary>
    public string Content { get; init; } = string.Empty;
    /// <summary>是否为错误</summary>
    [System.Text.Json.Serialization.JsonPropertyName("is_error")]
    public bool IsError { get; init; }
}

public sealed record SdkMessageBody {
    /// <summary>消息角色</summary>
    public string Role { get; init; } = "assistant";
    /// <summary>内容块数组</summary>
    public System.Collections.Generic.List<SdkContentBlock>? Content { get; init; }
}

/// <summary>SDK 消息基类 — 统一 Uuid + SessionId 字段，子类通过 Create() 工厂方法自动填充默认值</summary>
public abstract record SdkMessageBase {
    /// <summary>消息唯一标识</summary>
    public string? Uuid { get; init; }
    /// <summary>会话标识</summary>
    [System.Text.Json.Serialization.JsonPropertyName("session_id")]
    public string? SessionId { get; init; }
    /// <summary>生成新的标准 UUID 字符串</summary>
    protected static string NewUuid() => System.Guid.NewGuid().ToString();
}

public sealed record SdkAssistantMessage : SdkMessageBase {
    /// <summary>消息类型</summary>
    public string Type => "assistant";
    /// <summary>消息体</summary>
    public required SdkMessageBody Message { get; init; }
    /// <summary>父工具调用标识（子代理嵌套）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("parent_tool_use_id")]
    public string? ParentToolUseId { get; init; }
    /// <summary>错误类型（authentication_failed/billing_error/rate_limit/invalid_request/server_error/unknown/max_output_tokens）— 对齐基准侧 assistant.error 字段</summary>
    public string? Error { get; init; }
    /// <summary>创建带默认 Uuid 的实例</summary>
    public static SdkAssistantMessage Create(SdkMessageBody message, string? sessionId) => new() { Message = message, Uuid = NewUuid(), SessionId = sessionId };
}

public sealed record SdkUserMessage : SdkMessageBase {
    /// <summary>消息类型</summary>
    public string Type => "user";
    /// <summary>消息体</summary>
    public required SdkMessageBody Message { get; init; }
    /// <summary>父工具调用标识</summary>
    [System.Text.Json.Serialization.JsonPropertyName("parent_tool_use_id")]
    public string? ParentToolUseId { get; init; }
    /// <summary>结构化工具输出 — 对齐基准侧 tool_use_result（object 类型，C# 侧用 JsonElement 保留结构）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("tool_use_result")]
    public System.Text.Json.JsonElement? ToolUseResult { get; init; }
    /// <summary>是否为合成消息 — 对齐基准侧 isSynthetic 字段</summary>
    [System.Text.Json.Serialization.JsonPropertyName("isSynthetic")]
    public bool? IsSynthetic { get; init; }
    /// <summary>消息优先级（now/next/later）— 对齐基准侧 priority 字段</summary>
    public string? Priority { get; init; }
    /// <summary>ISO 时间戳 — 对齐基准侧 timestamp 字段</summary>
    public string? Timestamp { get; init; }
    /// <summary>创建带默认 Uuid 的实例</summary>
    public static SdkUserMessage Create(SdkMessageBody message, string? sessionId) => new() { Message = message, Uuid = NewUuid(), SessionId = sessionId };
}

public sealed record SdkMcpServer {
    /// <summary>服务器名称</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>连接状态</summary>
    public string Status { get; init; } = "connected";
}

/// <summary>插件信息 — 对齐基准侧 plugins 数组元素 {name, path, source?}</summary>
public sealed record SdkPlugin {
    /// <summary>插件名称</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>插件路径</summary>
    public string Path { get; init; } = string.Empty;
    /// <summary>插件来源（plugin/policy）</summary>
    public string? Source { get; init; }
}

public sealed record SdkSystemInitMessage : SdkMessageBase {
    /// <summary>消息类型</summary>
    public string Type => "system";
    /// <summary>消息子类型</summary>
    public string Subtype => "init";
    /// <summary>版本号</summary>
    [System.Text.Json.Serialization.JsonPropertyName("claude_code_version")]
    public string ClaudeCodeVersion { get; init; } = string.Empty;
    /// <summary>当前工作目录</summary>
    public string Cwd { get; init; } = string.Empty;
    /// <summary>模型标识</summary>
    public string Model { get; init; } = string.Empty;
    /// <summary>可用工具列表</summary>
    public System.Collections.Generic.List<string> Tools { get; init; } = [];
    /// <summary>MCP 服务器列表</summary>
    [System.Text.Json.Serialization.JsonPropertyName("mcp_servers")]
    public System.Collections.Generic.List<SdkMcpServer> McpServers { get; init; } = [];
    /// <summary>权限模式 — 对齐基准侧 permissionMode（camelCase）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("permissionMode")]
    public string PermissionMode { get; init; } = "default";
    /// <summary>技能列表</summary>
    public System.Collections.Generic.List<string> Skills { get; init; } = [];
    /// <summary>可用子代理列表 — 对齐基准侧 agents 字段</summary>
    public System.Collections.Generic.List<string> Agents { get; init; } = [];
    /// <summary>API Key 来源（user/project/org/temporary/oauth）— 对齐基准侧 apiKeySource 字段</summary>
    [System.Text.Json.Serialization.JsonPropertyName("apiKeySource")]
    public string ApiKeySource { get; init; } = "user";
    /// <summary>Beta 特性列表 — 对齐基准侧 betas 字段</summary>
    public System.Collections.Generic.List<string> Betas { get; init; } = [];
    /// <summary>斜杠命令列表 — 对齐基准侧 slash_commands 字段</summary>
    [System.Text.Json.Serialization.JsonPropertyName("slash_commands")]
    public System.Collections.Generic.List<string> SlashCommands { get; init; } = [];
    /// <summary>输出风格 — 对齐基准侧 output_style 字段</summary>
    [System.Text.Json.Serialization.JsonPropertyName("output_style")]
    public string OutputStyle { get; init; } = "default";
    /// <summary>插件列表 — 对齐基准侧 plugins 字段</summary>
    public System.Collections.Generic.List<SdkPlugin> Plugins { get; init; } = [];
    /// <summary>快速模式状态（off/cooldown/on）— 对齐基准侧 fast_mode_state 字段</summary>
    [System.Text.Json.Serialization.JsonPropertyName("fast_mode_state")]
    public string? FastModeState { get; init; }
    /// <summary>创建带默认 Uuid 的实例</summary>
    public static SdkSystemInitMessage Create(string? sessionId) => new() { Uuid = NewUuid(), SessionId = sessionId };
}

public sealed record SdkUsage {
    /// <summary>输入 token 数</summary>
    [System.Text.Json.Serialization.JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }
    /// <summary>输出 token 数</summary>
    [System.Text.Json.Serialization.JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }
    /// <summary>缓存创建 token 数</summary>
    [System.Text.Json.Serialization.JsonPropertyName("cache_creation_input_tokens")]
    public int? CacheCreationInputTokens { get; init; }
    /// <summary>缓存读取 token 数</summary>
    [System.Text.Json.Serialization.JsonPropertyName("cache_read_input_tokens")]
    public int? CacheReadInputTokens { get; init; }
}

public sealed record SdkModelUsage {
    /// <summary>输入 token 数</summary>
    [System.Text.Json.Serialization.JsonPropertyName("inputTokens")]
    public int InputTokens { get; init; }
    /// <summary>输出 token 数</summary>
    [System.Text.Json.Serialization.JsonPropertyName("outputTokens")]
    public int OutputTokens { get; init; }
    /// <summary>缓存读取 token 数</summary>
    [System.Text.Json.Serialization.JsonPropertyName("cacheReadInputTokens")]
    public int CacheReadInputTokens { get; init; }
    /// <summary>缓存创建 token 数</summary>
    [System.Text.Json.Serialization.JsonPropertyName("cacheCreationInputTokens")]
    public int CacheCreationInputTokens { get; init; }
    /// <summary>网络搜索请求数</summary>
    [System.Text.Json.Serialization.JsonPropertyName("webSearchRequests")]
    public int WebSearchRequests { get; init; }
    /// <summary>成本美元</summary>
    [System.Text.Json.Serialization.JsonPropertyName("costUSD")]
    public decimal CostUSD { get; init; }
    /// <summary>上下文窗口大小</summary>
    [System.Text.Json.Serialization.JsonPropertyName("contextWindow")]
    public int ContextWindow { get; init; }
    /// <summary>最大输出 token 数</summary>
    [System.Text.Json.Serialization.JsonPropertyName("maxOutputTokens")]
    public int MaxOutputTokens { get; init; }
}

public sealed record SdkPermissionDenial {
    /// <summary>被拒绝的工具名称</summary>
    [System.Text.Json.Serialization.JsonPropertyName("tool_name")]
    public string ToolName { get; init; } = string.Empty;
    /// <summary>工具调用标识</summary>
    [System.Text.Json.Serialization.JsonPropertyName("tool_use_id")]
    public string ToolUseId { get; init; } = string.Empty;
    /// <summary>工具输入参数</summary>
    [System.Text.Json.Serialization.JsonPropertyName("tool_input")]
    public System.Text.Json.JsonElement ToolInput { get; init; }
}

public sealed record SdkResultMessage : SdkMessageBase {
    /// <summary>消息类型</summary>
    public string Type => "result";
    /// <summary>结果子类型（success/error_during_execution 等）</summary>
    public string Subtype { get; init; } = "success";
    /// <summary>总耗时毫秒</summary>
    [System.Text.Json.Serialization.JsonPropertyName("duration_ms")]
    public long DurationMs { get; init; }
    /// <summary>API 耗时毫秒</summary>
    [System.Text.Json.Serialization.JsonPropertyName("duration_api_ms")]
    public long DurationApiMs { get; init; }
    /// <summary>是否错误</summary>
    [System.Text.Json.Serialization.JsonPropertyName("is_error")]
    public bool IsError { get; init; }
    /// <summary>对话轮次</summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_turns")]
    public int NumTurns { get; init; }
    /// <summary>最终结果文本 — error result 时为 null（序列化跳过），对齐基准侧 error result 无 result 字段</summary>
    [System.Text.Json.Serialization.JsonPropertyName("result")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Result { get; init; }
    /// <summary>停止原因</summary>
    [System.Text.Json.Serialization.JsonPropertyName("stop_reason")]
    public string? StopReason { get; init; }
    /// <summary>总成本美元</summary>
    [System.Text.Json.Serialization.JsonPropertyName("total_cost_usd")]
    public decimal TotalCostUsd { get; init; }
    /// <summary>token 用量</summary>
    public SdkUsage? Usage { get; init; }
    /// <summary>按模型统计的用量 — 对齐基准侧 modelUsage 字段</summary>
    [System.Text.Json.Serialization.JsonPropertyName("modelUsage")]
    public System.Collections.Generic.Dictionary<string, SdkModelUsage>? ModelUsage { get; init; }
    /// <summary>权限拒绝列表 — 对齐基准侧 permission_denials 字段</summary>
    [System.Text.Json.Serialization.JsonPropertyName("permission_denials")]
    public System.Collections.Generic.List<SdkPermissionDenial> PermissionDenials { get; init; } = [];
    /// <summary>错误消息列表 — error result 时填充</summary>
    public System.Collections.Generic.List<string> Errors { get; init; } = [];
    /// <summary>结构化输出 — 对齐基准侧 structured_output 字段（仅 success result）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("structured_output")]
    public System.Text.Json.JsonElement? StructuredOutput { get; init; }
    /// <summary>快速模式状态（off/cooldown/on）— 对齐基准侧 fast_mode_state 字段</summary>
    [System.Text.Json.Serialization.JsonPropertyName("fast_mode_state")]
    public string? FastModeState { get; init; }
    /// <summary>创建带默认 Uuid 的实例</summary>
    public static SdkResultMessage Create(string? sessionId) => new() { Uuid = NewUuid(), SessionId = sessionId };
}

public sealed record SdkToolProgressMessage : SdkMessageBase {
    /// <summary>消息类型</summary>
    public string Type => "tool_progress";
    /// <summary>工具调用标识</summary>
    [System.Text.Json.Serialization.JsonPropertyName("tool_use_id")]
    public string ToolUseId { get; init; } = string.Empty;
    /// <summary>工具名称</summary>
    [System.Text.Json.Serialization.JsonPropertyName("tool_name")]
    public string ToolName { get; init; } = string.Empty;
    /// <summary>父工具调用标识</summary>
    [System.Text.Json.Serialization.JsonPropertyName("parent_tool_use_id")]
    public string? ParentToolUseId { get; init; }
    /// <summary>已用秒数</summary>
    [System.Text.Json.Serialization.JsonPropertyName("elapsed_time_seconds")]
    public double ElapsedTimeSeconds { get; init; }
    /// <summary>任务标识 — 对齐基准侧 task_id 字段</summary>
    [System.Text.Json.Serialization.JsonPropertyName("task_id")]
    public string? TaskId { get; init; }
    /// <summary>创建带默认 Uuid 的实例</summary>
    public static SdkToolProgressMessage Create(string? sessionId) => new() { Uuid = NewUuid(), SessionId = sessionId };
}

/// <summary>会话状态变更消息 — 对齐基准侧 system/session_state_changed 事件，state: idle|running|requires_action</summary>
public sealed record SdkSessionStateChangedMessage : SdkMessageBase {
    /// <summary>消息类型</summary>
    public string Type => "system";
    /// <summary>消息子类型</summary>
    public string Subtype => "session_state_changed";
    /// <summary>会话状态（idle/running/requires_action）— 对齐基准侧 state 字段</summary>
    public string State { get; init; } = "idle";
    /// <summary>创建带默认 Uuid 的实例</summary>
    public static SdkSessionStateChangedMessage Create(string? sessionId, string state) => new() { Uuid = NewUuid(), SessionId = sessionId, State = state };
}
