namespace JoinCode.Abstractions.Hooks.Session;

/// <summary>
/// Swarm 权限回调接口 — 用于 Swarm Worker 向 Leader 转发权限请求
/// </summary>
public interface ISwarmPermissionCallbacks {
    /// <summary>创建权限请求。</summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="toolUseId">工具调用标识。</param>
    /// <param name="input">输入参数。</param>
    /// <param name="description">描述。</param>
    /// <param name="suggestions">权限建议列表。</param>
    SwarmPermissionRequest CreatePermissionRequest(string toolName, string toolUseId, Dictionary<string, JsonElement> input, string description, List<PermissionUpdate>? suggestions);

    /// <summary>通过邮箱异步发送权限请求。</summary>
    /// <param name="request">权限请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendPermissionRequestViaMailboxAsync(SwarmPermissionRequest request, CancellationToken cancellationToken = default);

    /// <summary>注册权限回调。</summary>
    /// <param name="callback">权限回调。</param>
    void RegisterPermissionCallback(SwarmPermissionCallback callback);
}

/// <summary>
/// Swarm 权限回调处理接口 — 扩展 ISwarmPermissionCallbacks，补充消息处理和请求查询能力
/// </summary>
public interface ISwarmPermissionCallbackHandler : ISwarmPermissionCallbacks {
    /// <summary>处理权限响应 — 执行已注册的回调（允许或拒绝）。</summary>
    /// <param name="requestId">请求标识。</param>
    /// <param name="allowed">是否允许。</param>
    /// <param name="updatedInput">更新后的工具输入参数。</param>
    /// <param name="permissionUpdates">权限更新列表。</param>
    /// <param name="feedback">反馈信息。</param>
    Task HandlePermissionResponseAsync(string requestId, bool allowed, Dictionary<string, JsonElement>? updatedInput, List<PermissionUpdate>? permissionUpdates, string? feedback);

    /// <summary>获取指定请求的待处理权限请求。</summary>
    /// <param name="requestId">请求唯一标识。</param>
    /// <returns>待处理权限请求，不存在则返回 null。</returns>
    SwarmPermissionRequest? GetPendingRequest(string requestId);

    /// <summary>异步处理收到的权限响应消息。</summary>
    /// <param name="message">收到的协调器消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ProcessIncomingResponseMessageAsync(CoordinatorMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Swarm 权限请求
/// </summary>
public sealed record SwarmPermissionRequest {
    /// <summary>获取请求标识。</summary>
    public required string Id { get; init; }
    /// <summary>获取工具名称。</summary>
    public required string ToolName { get; init; }
    /// <summary>获取工具调用标识。</summary>
    public required string ToolUseId { get; init; }
    /// <summary>获取输入参数。</summary>
    public required Dictionary<string, JsonElement> Input { get; init; }
    /// <summary>获取描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取权限建议列表。</summary>
    public List<PermissionUpdate>? PermissionSuggestions { get; init; }
}

/// <summary>
/// Swarm 权限回调
/// </summary>
public sealed record SwarmPermissionCallback {
    /// <summary>获取请求标识。</summary>
    public required string RequestId { get; init; }
    /// <summary>获取工具调用标识。</summary>
    public required string ToolUseId { get; init; }
    /// <summary>获取允许回调。</summary>
    public required Func<Dictionary<string, JsonElement>?, List<PermissionUpdate>?, string?, Task> OnAllow { get; init; }
    /// <summary>获取拒绝回调。</summary>
    public required Func<string?, Task> OnReject { get; init; }
}
