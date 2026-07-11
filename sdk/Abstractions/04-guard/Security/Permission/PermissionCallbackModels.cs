namespace JoinCode.Abstractions.Security.Permission;

/// <summary>
/// 权限回调响应 — 跨组件权限交互的响应数据
/// </summary>
public sealed class PermissionCallbackResponse
{
    [JsonPropertyName("behavior")]
    public required string Behavior { get; init; }

    [JsonPropertyName("updated_input")]
    public Dictionary<string, JsonElement>? UpdatedInput { get; init; }

    [JsonPropertyName("updated_permissions")]
    public List<PermissionCallbackUpdate>? UpdatedPermissions { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// 权限回调更新 — 跨组件权限交互的更新建议
/// </summary>
public sealed class PermissionCallbackUpdate
{
    [JsonPropertyName("tool_name")]
    public string? ToolName { get; init; }

    [JsonPropertyName("permission_mode")]
    public string? PermissionMode { get; init; }
}
