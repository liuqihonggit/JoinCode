namespace JoinCode.Abstractions.Hooks;

public sealed record PermissionUpdate
{
    public required string ToolName { get; init; }
    public required string Action { get; init; }
    public required string Destination { get; init; }
    public Dictionary<string, JsonElement>? Parameters { get; init; }
}

/// <summary>
/// 钩子输入数据（纯数据部分，不含工厂方法）
/// </summary>
public sealed record HookInput
{
    public required HookEvent Event { get; init; }

    public string EventName => Event.ToEventName();

    public required Dictionary<string, JsonElement> Payload { get; init; }

    public string? Matcher { get; init; }

    public string? ToolName { get; init; }

    public string? ToolUseId { get; init; }

    public string? SessionId { get; init; }

    public string? PluginId { get; init; }

    public Func<string, Task>? OnModelWake { get; init; }
}
