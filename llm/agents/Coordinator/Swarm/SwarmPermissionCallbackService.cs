
namespace Core.Agents.Coordinator;

/// <summary>
/// Swarm 权限请求数据 — Worker 向 Leader 发起的工具权限请求
/// </summary>
public sealed partial class SwarmPermissionRequestData {
    /// <summary>
    /// 请求唯一标识
    /// </summary>
    public required string RequestId { get; init; }
    /// <summary>
    /// 工具名称
    /// </summary>
    public required string ToolName { get; init; }
    /// <summary>
    /// 工具调用唯一标识
    /// </summary>
    public required string ToolUseId { get; init; }
    /// <summary>
    /// 工具调用描述
    /// </summary>
    public required string Description { get; init; }
    /// <summary>
    /// 发起请求的 Worker 智能体标识
    /// </summary>
    public required string WorkerAgentId { get; init; }
}

/// <summary>
/// Swarm 权限响应数据 — Leader 对 Worker 权限请求的回复
/// </summary>
public sealed partial class SwarmPermissionResponseData {
    /// <summary>
    /// 对应请求的唯一标识
    /// </summary>
    public required string RequestId { get; init; }
    /// <summary>
    /// 权限行为（allow/deny）
    /// </summary>
    public required string Behavior { get; init; }
    /// <summary>
    /// 反馈信息（拒绝时使用）
    /// </summary>
    public string? Feedback { get; init; }
    /// <summary>
    /// 更新后的工具输入参数
    /// </summary>
    public Dictionary<string, JsonElement> UpdatedInput { get; init; } = [];
    /// <summary>
    /// 权限更新列表
    /// </summary>
    public List<SwarmPermissionUpdateData> PermissionUpdates { get; init; } = [];
}

/// <summary>
/// Swarm 权限更新数据 — 单个工具的权限变更条目
/// </summary>
public sealed partial class SwarmPermissionUpdateData {
    /// <summary>
    /// 工具名称
    /// </summary>
    public required string ToolName { get; init; }
    /// <summary>
    /// 权限行为（允许或拒绝）
    /// </summary>
    public required PermissionBehavior Action { get; init; }
}

/// <summary>Swarm 权限回调服务 — 管理权限请求的回调注册与触发，处理权限决策结果的通知分发</summary>
[Register(typeof(ISwarmPermissionCallbacks), ServiceLifetime.Singleton)]
public sealed partial class SwarmPermissionCallbackService : ServiceEntity, ISwarmPermissionCallbacks {
    private readonly IMailbox _messageBroker;
    private readonly ILogger<SwarmPermissionCallbackService>? _logger;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly ConcurrentDictionary<string, SwarmPermissionCallback> _pendingCallbacks;
    private readonly ConcurrentDictionary<string, SwarmPermissionRequest> _pendingRequests;

    /// <summary>
    /// 初始化 Swarm 权限回调服务
    /// </summary>
    /// <param name="messageBroker">消息邮箱</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="subAgentContextAccessor">子智能体上下文访问器</param>
    public SwarmPermissionCallbackService(
        IMailbox messageBroker,
        ILogger<SwarmPermissionCallbackService>? logger = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null) {
        _messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
        _logger = logger;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _pendingCallbacks = new ConcurrentDictionary<string, SwarmPermissionCallback>();
        _pendingRequests = new ConcurrentDictionary<string, SwarmPermissionRequest>();
    }

    /// <summary>
    /// 创建权限请求并加入待处理队列
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="toolUseId">工具调用唯一标识</param>
    /// <param name="input">工具输入参数</param>
    /// <param name="description">工具调用描述</param>
    /// <param name="suggestions">权限建议列表</param>
    /// <returns>创建的权限请求</returns>
    public SwarmPermissionRequest CreatePermissionRequest(
        string toolName,
        string toolUseId,
        Dictionary<string, JsonElement> input,
        string description,
        List<PermissionUpdate>? suggestions) {
        var request = new SwarmPermissionRequest {
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

    /// <summary>
    /// 异步通过邮箱发送权限请求到 Leader
    /// </summary>
    /// <param name="request">权限请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task SendPermissionRequestViaMailboxAsync(
        SwarmPermissionRequest request,
        CancellationToken cancellationToken = default) {
        var workerAgentId = _subAgentContextAccessor.Current?.AgentId ?? "unknown";
        var leaderAgentId = _subAgentContextAccessor.Current?.ParentAgentId ?? "coordinator";

        var data = new SwarmPermissionRequestData {
            RequestId = request.Id,
            ToolName = request.ToolName,
            ToolUseId = request.ToolUseId,
            Description = request.Description,
            WorkerAgentId = workerAgentId
        };

        var content = JsonSerializer.Serialize(data, AgentsJsonContext.Default.SwarmPermissionRequestData);

        var message = new CoordinatorAgentMessage {
            FromAgentId = workerAgentId,
            ToAgentId = leaderAgentId,
            MessageType = SwarmPermissionMessageType.PermissionRequest.ToValue(),
            Content = content
        };

        await _messageBroker.SendAsync(leaderAgentId, message, cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation(
            "权限请求已发送到 Leader: RequestId={RequestId}, Tool={ToolName}, Leader={LeaderId}",
            request.Id, request.ToolName, leaderAgentId);
    }

    /// <summary>
    /// 注册权限回调，等待 Leader 响应
    /// </summary>
    /// <param name="callback">权限回调</param>
    public void RegisterPermissionCallback(SwarmPermissionCallback callback) {
        _pendingCallbacks[callback.RequestId] = callback;

        _logger?.LogDebug("注册权限回调: RequestId={RequestId}", callback.RequestId);
    }

    /// <summary>
    /// 异步处理权限响应，触发对应回调的允许或拒绝分支
    /// </summary>
    /// <param name="requestId">请求唯一标识</param>
    /// <param name="allowed">是否允许</param>
    /// <param name="updatedInput">更新后的工具输入</param>
    /// <param name="permissionUpdates">权限更新列表</param>
    /// <param name="feedback">反馈信息</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task HandlePermissionResponseAsync(
        string requestId,
        bool allowed,
        Dictionary<string, JsonElement>? updatedInput,
        List<PermissionUpdate>? permissionUpdates,
        string? feedback) {
        if (!_pendingCallbacks.TryRemove(requestId, out var callback)) {
            _logger?.LogWarning("未找到权限回调: RequestId={RequestId}", requestId);
            return;
        }

        _pendingRequests.TryRemove(requestId, out _);

        _logger?.LogInformation(
            "处理权限响应: RequestId={RequestId}, Allowed={Allowed}",
            requestId, allowed);

        if (allowed) {
            await callback.OnAllow(updatedInput, permissionUpdates, feedback).ConfigureAwait(false);
        } else {
            await callback.OnReject(feedback).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 获取指定请求的待处理权限请求
    /// </summary>
    /// <param name="requestId">请求唯一标识</param>
    /// <returns>待处理权限请求，不存在则返回 null</returns>
    public SwarmPermissionRequest? GetPendingRequest(string requestId) {
        return _pendingRequests.GetValueOrDefault(requestId);
    }

    /// <summary>
    /// 异步处理收到的权限响应消息，反序列化后调用 HandlePermissionResponseAsync
    /// </summary>
    /// <param name="message">收到的协调器消息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ProcessIncomingResponseMessageAsync(CoordinatorAgentMessage message, CancellationToken ct = default) {
        if (message.MessageType != SwarmPermissionMessageType.PermissionResponse.ToValue()) {
            return;
        }

        try {
            var data = RelaxedJsonSerializer.Deserialize(
                message.Content,
                AgentsJsonContext.Default.SwarmPermissionResponseData);

            if (data == null) {
                _logger?.LogWarning("无法反序列化权限响应: From={FromId}", message.FromAgentId);
                return;
            }

            var allowed = data.Behavior == PermissionBehaviorEnumConstants.Allow;

            var permissionUpdates = data.PermissionUpdates?.ConvertAll(pu =>
                new PermissionUpdate { ToolName = pu.ToolName, Action = pu.Action.ToValue(), Destination = "session" });

            await HandlePermissionResponseAsync(
                data.RequestId,
                allowed,
                data.UpdatedInput,
                permissionUpdates,
                data.Feedback).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogError(ex, "处理权限响应消息失败: From={FromId}", message.FromAgentId);
        }
    }

    /// <summary>
    /// 当前待处理权限请求的数量
    /// </summary>
    public int PendingRequestCount => _pendingRequests.Count;
}