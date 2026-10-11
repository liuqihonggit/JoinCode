namespace McpToolRegistry;

/// <summary>
/// LLM 工具注册表事件接口 - 用于通知工具变更事件
/// </summary>
public interface ILlmToolRegistryEvents {
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
public sealed class ToolRegisteredEventArgs : EventArgs {
    /// <summary>工具名称</summary>
    public required string ToolName { get; init; }

    /// <summary>工具描述</summary>
    public required string Description { get; init; }

    /// <summary>注册时间(UTC)</summary>
    public DateTime RegisteredAt { get; } = DateTime.UtcNow;
}

/// <summary>
/// 工具注销事件参数
/// </summary>
public sealed class ToolUnregisteredEventArgs : EventArgs {
    /// <summary>工具名称</summary>
    public required string ToolName { get; init; }

    /// <summary>注销时间(UTC)</summary>
    public DateTime UnregisteredAt { get; } = DateTime.UtcNow;
}