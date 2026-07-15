
namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// Agent响应
/// </summary>
public class AgentResponse
{
    public string Content { get; set; } = string.Empty;
    public bool IsToolCall { get; set; }
    public List<ToolCall> ToolCalls { get; set; } = new();
    public TokenUsage TokenUsage { get; set; } = new();
    public long ExecutionTimeMs { get; set; }
}

public class ToolCall
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, JsonElement> Arguments { get; set; } = new();
    public string? Result { get; set; }
}

public class ToolDefinition : NamedItem
{
    public Dictionary<string, ToolParameter> Parameters { get; set; } = new();
}

public class ToolParameter : SchemaProperty
{
    public bool Required { get; set; } = true;
}

public class AgentContext
{
    public List<AgentMessage> Messages { get; set; } = new();
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public int TotalToolCalls { get; set; }
    public TokenUsage TotalTokenUsage { get; set; } = new();

    /// <summary>
    /// 上下文层级管理器 - 支持分层上下文压缩
    /// </summary>
    public object? ContextHierarchy { get; set; }
}

public class AgentMessage : ChatMessage
{
    public List<ToolCall>? ToolCalls { get; set; }
}
