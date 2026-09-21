namespace JoinCode.Abstractions.Security.Permission;

/// <summary>
/// 权限回调响应 — 跨组件权限交互的响应数据
/// </summary>
public sealed class PermissionCallbackResponse {
    /// <summary>获取行为类型。</summary>
    [JsonPropertyName("behavior")]
    public required string Behavior { get; init; }

    /// <summary>获取更新后的输入。</summary>
    [JsonPropertyName("updated_input")]
    public Dictionary<string, JsonElement> UpdatedInput { get; init; } = [];

    /// <summary>获取更新后的权限列表。</summary>
    [JsonPropertyName("updated_permissions")]
    public List<PermissionCallbackUpdate> UpdatedPermissions { get; init; } = [];

    /// <summary>获取消息。</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// 权限回调更新 — 跨组件权限交互的更新建议
/// </summary>
public sealed class PermissionCallbackUpdate {
    /// <summary>获取工具名称。</summary>
    [JsonPropertyName("tool_name")]
    public string? ToolName { get; init; }

    /// <summary>获取权限模式。</summary>
    [JsonPropertyName("permission_mode")]
    public string? PermissionMode { get; init; }
}