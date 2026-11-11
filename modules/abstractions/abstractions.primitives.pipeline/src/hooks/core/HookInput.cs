namespace JoinCode.Abstractions.Hooks;

public sealed record PermissionUpdate {
    /// <summary>获取工具名称。</summary>
    public required string ToolName { get; init; }
    /// <summary>获取动作名称。</summary>
    public required string Action { get; init; }
    /// <summary>获取目标名称。</summary>
    public required string Destination { get; init; }
    /// <summary>获取参数字典。</summary>
    public Dictionary<string, JsonElement>? Parameters { get; init; }
}

/// <summary>
/// 钩子输入数据（纯数据部分，不含工厂方法）
/// </summary>
public sealed record HookInput {
    /// <summary>获取钩子事件类型。</summary>
    public required HookEvent Event { get; init; }

    /// <summary>获取事件名称。</summary>
    public string EventName => Event.ToEventName();

    /// <summary>获取事件负载字典。</summary>
    public required Dictionary<string, JsonElement> Payload { get; init; }

    /// <summary>获取匹配器表达式。</summary>
    public string? Matcher { get; init; }

    /// <summary>获取工具名称。</summary>
    public string? ToolName { get; init; }

    /// <summary>获取工具使用标识。</summary>
    public string? ToolUseId { get; init; }

    /// <summary>获取会话标识。</summary>
    public string? SessionId { get; init; }

    /// <summary>获取插件标识。</summary>
    public string? PluginId { get; init; }

    /// <summary>获取模型唤醒回调。</summary>
    public Func<string, Task>? OnModelWake { get; init; }
}
