
namespace Core.Agents.Coordinator;

public sealed partial class SwarmPermissionRequestData
{
    public required string RequestId { get; init; }
    public required string ToolName { get; init; }
    public required string ToolUseId { get; init; }
    public required string Description { get; init; }
    public required string WorkerAgentId { get; init; }
}

public sealed partial class SwarmPermissionResponseData
{
    public required string RequestId { get; init; }
    public required string Behavior { get; init; }
    public string? Feedback { get; init; }
    public Dictionary<string, JsonElement>? UpdatedInput { get; init; }
    public List<SwarmPermissionUpdateData>? PermissionUpdates { get; init; }
}

public sealed partial class SwarmPermissionUpdateData
{
    public required string ToolName { get; init; }
    public required PermissionBehavior Action { get; init; }
}

[Register]
[Register]
public sealed partial class SwarmPermissionCallbackService : ISwarmPermissionCallbacks
{
    private readonly IAgentMessageBroker _messageBroker;
    [Inject] private readonly ILogger<SwarmPermissionCallbackService>? _logger;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly ConcurrentDictionary<string, SwarmPermissionCallback> _pendingCallbacks;
    private readonly ConcurrentDictionary<string, SwarmPermissionRequest> _pendingRequests;

    public SwarmPermissionCallbackService(
        IAgentMessageBroker messageBroker,
        ILogger<SwarmPermissionCallbackService>? logger = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null)
    {
        _messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
        _logger = logger;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _pendingCallbacks = new ConcurrentDictionary<string, SwarmPermissionCallback>();
        _pendingRequests = new ConcurrentDictionary<string, SwarmPermissionRequest>();
    }

    public SwarmPermissionRequest CreatePermissionRequest(
        string toolName,
        string toolUseId,
        Dictionary<string, JsonElement> input,
        string description,
        List<PermissionUpdate>? suggestions)
    {
        var request = new SwarmPermissionRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            ToolName = toolName,
            ToolUseId = toolUseId,
            Input = input,
            Description = description,
            PermissionSuggestions = suggestions
        };

        _pendingRequests[request.Id] = request;

        _logger?.LogDebug("创建权限请求: RequestId={RequestId}, Tool={ToolName}", request.Id, toolName);

        return request;
    }

    public async Task SendPermissionRequestViaMailboxAsync(
        SwarmPermissionRequest request,
        CancellationToken cancellationToken = default)
    {
        var workerAgentId = _subAgentContextAccessor.Current?.AgentId ?? "unknown";
        var leaderAgentId = _subAgentContextAccessor.Current?.ParentAgentId ?? "coordinator";

        var data = new SwarmPermissionRequestData
        {
            RequestId = request.Id,
            ToolName = request.ToolName,
            ToolUseId = request.ToolUseId,
            Description = request.Description,
            WorkerAgentId = workerAgentId
        };

        var content = JsonSerializer.Serialize(data, AgentsJsonContext.Default.SwarmPermissionRequestData);

        var message = new CoordinatorAgentMessage
        {
            FromAgentId = workerAgentId,
            ToAgentId = leaderAgentId,
            MessageType = SwarmPermissionMessageType.PermissionRequest.ToValue(),
            Content = content
        };

        await _messageBroker.SendMessageAsync(leaderAgentId, message, cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation(
            "权限请求已发送到 Leader: RequestId={RequestId}, Tool={ToolName}, Leader={LeaderId}",
            request.Id, request.ToolName, leaderAgentId);
    }

    public void RegisterPermissionCallback(SwarmPermissionCallback callback)
    {
        _pendingCallbacks[callback.RequestId] = callback;

        _logger?.LogDebug("注册权限回调: RequestId={RequestId}", callback.RequestId);
    }

    public async Task HandlePermissionResponseAsync(
        string requestId,
        bool allowed,
        Dictionary<string, JsonElement>? updatedInput,
        List<PermissionUpdate>? permissionUpdates,
        string? feedback)
    {
        if (!_pendingCallbacks.TryRemove(requestId, out var callback))
        {
            _logger?.LogWarning("未找到权限回调: RequestId={RequestId}", requestId);
            return;
        }

        _pendingRequests.TryRemove(requestId, out _);

        _logger?.LogInformation(
            "处理权限响应: RequestId={RequestId}, Allowed={Allowed}",
            requestId, allowed);

        if (allowed)
        {
            await callback.OnAllow(updatedInput, permissionUpdates, feedback).ConfigureAwait(false);
        }
        else
        {
            await callback.OnReject(feedback).ConfigureAwait(false);
        }
    }

    public SwarmPermissionRequest? GetPendingRequest(string requestId)
    {
        return _pendingRequests.GetValueOrDefault(requestId);
    }

    public async Task ProcessIncomingResponseMessageAsync(CoordinatorAgentMessage message, CancellationToken ct = default)
    {
        if (message.MessageType != SwarmPermissionMessageType.PermissionResponse.ToValue())
        {
            return;
        }

        try
        {
            var data = JsonSerializer.Deserialize(
                message.Content,
                AgentsJsonContext.Default.SwarmPermissionResponseData);

            if (data == null)
            {
                _logger?.LogWarning("无法反序列化权限响应: From={FromId}", message.FromAgentId);
                return;
            }

            var allowed = data.Behavior == PermissionBehaviorConstants.Allow;

            var permissionUpdates = data.PermissionUpdates?.ConvertAll(pu =>
                new PermissionUpdate { ToolName = pu.ToolName, Action = pu.Action.ToValue(), Destination = "session" });

            await HandlePermissionResponseAsync(
                data.RequestId,
                allowed,
                data.UpdatedInput,
                permissionUpdates,
                data.Feedback).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理权限响应消息失败: From={FromId}", message.FromAgentId);
        }
    }

    public int PendingRequestCount => _pendingRequests.Count;
}

