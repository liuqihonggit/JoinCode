namespace McpToolRegistry;

/// <summary>
/// LLM 工具注册表事件接口 - 用于通知工具变更事件
/// </summary>
public interface ILlmToolRegistryEvents
{
    /// <summary>
    /// 工具已注册事件
    /// </summary>
    event EventHandler<ToolRegisteredEventArgs>? ToolRegistered;

    /// <summary>
    /// 工具已注销事件
    /// </summary>
    event EventHandler<ToolUnregisteredEventArgs>? ToolUnregistered;

    /// <summary>
    /// 所有工具已清除事件
    /// </summary>
    event EventHandler? ToolsCleared;
}

/// <summary>
/// 工具注册事件参数
/// </summary>
public sealed class ToolRegisteredEventArgs : EventArgs
{
    public required string ToolName { get; init; }
    public required string Description { get; init; }
    public DateTime RegisteredAt { get; } = DateTime.UtcNow;
}

/// <summary>
/// 工具注销事件参数
/// </summary>
public sealed class ToolUnregisteredEventArgs : EventArgs
{
    public required string ToolName { get; init; }
    public DateTime UnregisteredAt { get; } = DateTime.UtcNow;
}
