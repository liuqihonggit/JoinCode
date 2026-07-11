namespace JoinCode.Abstractions.Hooks.Session;

/// <summary>
/// Swarm 权限回调接口 — 用于 Swarm Worker 向 Leader 转发权限请求
/// </summary>
public interface ISwarmPermissionCallbacks
{
    SwarmPermissionRequest CreatePermissionRequest(string toolName, string toolUseId, Dictionary<string, JsonElement> input, string description, List<PermissionUpdate>? suggestions);

    Task SendPermissionRequestViaMailboxAsync(SwarmPermissionRequest request, CancellationToken cancellationToken = default);

    void RegisterPermissionCallback(SwarmPermissionCallback callback);
}

/// <summary>
/// Swarm 权限请求
/// </summary>
public sealed record SwarmPermissionRequest
{
    public required string Id { get; init; }
    public required string ToolName { get; init; }
    public required string ToolUseId { get; init; }
    public required Dictionary<string, JsonElement> Input { get; init; }
    public required string Description { get; init; }
    public List<PermissionUpdate>? PermissionSuggestions { get; init; }
}

/// <summary>
/// Swarm 权限回调
/// </summary>
public sealed record SwarmPermissionCallback
{
    public required string RequestId { get; init; }
    public required string ToolUseId { get; init; }
    public required Func<Dictionary<string, JsonElement>?, List<PermissionUpdate>?, string?, Task> OnAllow { get; init; }
    public required Func<string?, Task> OnReject { get; init; }
}
