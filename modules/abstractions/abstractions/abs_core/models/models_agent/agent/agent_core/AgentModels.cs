
namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// Agent响应
/// </summary>
public class AgentResponse {
    /// <summary>获取或设置响应内容。</summary>
    public string Content { get; set; } = string.Empty;
    /// <summary>获取或设置是否为工具调用。</summary>
    public bool IsToolCall { get; set; }
    /// <summary>获取或设置工具调用列表。</summary>
    public List<ToolCall> ToolCalls { get; set; } = new();
    /// <summary>获取或设置令牌用量。</summary>
    public TokenUsage TokenUsage { get; set; } = new();
    /// <summary>获取或设置执行耗时(毫秒)。</summary>
    public long ExecutionTimeMs { get; set; }
}

public class ToolCall {
    /// <summary>获取或设置调用标识。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>获取或设置工具名称。</summary>
    public string ToolName { get; set; } = string.Empty;
    /// <summary>获取或设置调用参数。</summary>
    public Dictionary<string, JsonElement> Arguments { get; set; } = new();
    /// <summary>获取或设置调用结果。</summary>
    public string? Result { get; set; }
}

public class ToolDefinition : NamedItem {
    /// <summary>获取或设置工具参数字典。</summary>
    public Dictionary<string, ToolParameter> Parameters { get; set; } = new();
}

public class ToolParameter : SchemaProperty {
    /// <summary>获取或设置是否必填。</summary>
    public bool Required { get; set; } = true;
}

public class AgentContext {
    /// <summary>获取或设置消息列表。</summary>
    public List<AgentMessage> Messages { get; set; } = new();
    /// <summary>获取或设置开始时间。</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    /// <summary>获取或设置工具调用总数。</summary>
    public int TotalToolCalls { get; set; }
    /// <summary>获取或设置累计令牌用量。</summary>
    public TokenUsage TotalTokenUsage { get; set; } = new();

    /// <summary>
    /// 上下文层级管理器 - 支持分层上下文压缩
    /// </summary>
    public object? ContextHierarchy { get; set; }
}

public class AgentMessage : ChatMessage {
    /// <summary>获取或设置工具调用列表。</summary>
    public List<ToolCall> ToolCalls { get; set; } = [];
}