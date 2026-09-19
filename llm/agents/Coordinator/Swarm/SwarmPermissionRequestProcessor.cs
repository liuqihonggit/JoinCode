
namespace Core.Agents.Coordinator;

/// <summary>
/// Swarm 权限请求处理器接口 — 处理 Worker 子智能体发起的工具权限请求
/// </summary>
public interface ISwarmPermissionRequestProcessor {
    /// <summary>
    /// 异步处理权限请求，评估并回复允许或拒绝
    /// </summary>
    /// <param name="requestData">权限请求数据</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task ProcessRequestAsync(SwarmPermissionRequestData requestData, CancellationToken ct = default);
}

/// <summary>Swarm 权限请求处理器 — 处理权限请求的核心逻辑，与权限管理器和回调服务协作完成权限审批</summary>
[Register(typeof(ISwarmPermissionRequestProcessor), ServiceLifetime.Singleton)]
public sealed partial class SwarmPermissionRequestProcessor : ServiceEntity, ISwarmPermissionRequestProcessor {
    private readonly IMailbox _messageBroker;
    private readonly IAgentPermissionManager _permissionManager;
    private readonly SwarmPermissionCallbackService _callbackService;
    private readonly ILogger<SwarmPermissionRequestProcessor>? _logger;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;

    /// <summary>
    /// 初始化权限请求处理器
    /// </summary>
    /// <param name="messageBroker">消息邮箱</param>
    /// <param name="permissionManager">权限管理器</param>
    /// <param name="callbackService">权限回调服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="subAgentContextAccessor">子智能体上下文访问器</param>
    public SwarmPermissionRequestProcessor(
        IMailbox messageBroker,
        IAgentPermissionManager permissionManager,
        SwarmPermissionCallbackService callbackService,
        ILogger<SwarmPermissionRequestProcessor>? logger = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null) {
        _messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
        _permissionManager = permissionManager ?? throw new ArgumentNullException(nameof(permissionManager));
        _callbackService = callbackService ?? throw new ArgumentNullException(nameof(callbackService));
        _logger = logger;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
    }

    /// <summary>
    /// 异步处理权限请求，评估后通过邮箱回复 Worker 允许或拒绝
    /// </summary>
    /// <param name="requestData">权限请求数据</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ProcessRequestAsync(SwarmPermissionRequestData requestData, CancellationToken ct = default) {
        _logger?.LogInformation(
            "处理权限请求: RequestId={RequestId}, Tool={ToolName}, Worker={WorkerId}",
            requestData.RequestId, requestData.ToolName, requestData.WorkerAgentId);

        var (allowed, updatedInput, permissionUpdates) = await EvaluatePermissionAsync(requestData, ct).ConfigureAwait(false);

        var responseData = new SwarmPermissionResponseData {
            RequestId = requestData.RequestId,
            Behavior = allowed ? PermissionBehaviorEnumConstants.Allow : PermissionBehaviorEnumConstants.Deny,
            Feedback = allowed ? null : $"Leader denied: {requestData.ToolName}",
            UpdatedInput = allowed ? (updatedInput ?? []) : [],
            PermissionUpdates = allowed ? (permissionUpdates ?? []) : []
        };

        var content = JsonSerializer.Serialize(responseData, AgentsJsonContext.Default.SwarmPermissionResponseData);

        var coordinatorId = _subAgentContextAccessor.Current?.AgentId ?? "coordinator";

        var message = new CoordinatorAgentMessage {
            FromAgentId = coordinatorId,
            ToAgentId = requestData.WorkerAgentId,
            MessageType = SwarmPermissionMessageType.PermissionResponse.ToValue(),
            Content = content
        };

        await _messageBroker.SendAsync(requestData.WorkerAgentId, message, ct).ConfigureAwait(false);

        _logger?.LogInformation(
            "权限响应已发送: RequestId={RequestId}, Decision={Decision}, Worker={WorkerId}",
            requestData.RequestId, allowed ? PermissionBehaviorEnumConstants.Allow : PermissionBehaviorEnumConstants.Deny, requestData.WorkerAgentId);
    }

    private async Task<(bool Allowed, Dictionary<string, JsonElement>? UpdatedInput, List<SwarmPermissionUpdateData>? PermissionUpdates)> EvaluatePermissionAsync(
        SwarmPermissionRequestData requestData, CancellationToken ct) {
        var fullRequest = _callbackService.GetPendingRequest(requestData.RequestId);
        var toolName = requestData.ToolName;

        if (IsAutoApprovedTool(toolName)) {
            _logger?.LogDebug("权限自动批准: Tool={ToolName} (自动批准列表)", toolName);
            return (true, fullRequest?.Input, null);
        }

        if (IsDangerousTool(toolName)) {
            _logger?.LogDebug("权限拒绝: Tool={ToolName} (危险工具)", toolName);
            return (false, null, null);
        }

        try {
            var checkResult = await _permissionManager.CheckToolPermissionAsync(
                requestData.WorkerAgentId,
                toolName,
                null,
                ct).ConfigureAwait(false);

            if (checkResult.IsAllowed && checkResult.Mode != PermissionMode.Ask) {
                _logger?.LogDebug("权限由规则批准: Tool={ToolName}, Mode={Mode}", toolName, checkResult.Mode);

                List<SwarmPermissionUpdateData>? updates = null;
                if (checkResult.MatchedRule != null) {
                    updates = BuildPermissionUpdatesFromRule(checkResult.MatchedRule);
                }

                return (true, fullRequest?.Input, updates);
            }

            if (checkResult.Mode == PermissionMode.Ask && !checkResult.IsAllowed) {
                _logger?.LogDebug("权限由规则拒绝: Tool={ToolName}", toolName);
                return (false, null, null);
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, "权限规则检查失败: Tool={ToolName}", toolName);
        }

        _logger?.LogDebug("权限未自动解决，保守拒绝: Tool={ToolName}", toolName);
        return (false, null, null);
    }

    private static List<SwarmPermissionUpdateData> BuildPermissionUpdatesFromRule(AgentPermissionRule rule) {
        var updates = new List<SwarmPermissionUpdateData>();

        if (rule.AllowedTools != null) {
            foreach (var tool in rule.AllowedTools) {
                updates.Add(new SwarmPermissionUpdateData { ToolName = tool, Action = PermissionBehavior.Allow });
            }
        }

        if (rule.DeniedTools != null) {
            foreach (var tool in rule.DeniedTools) {
                updates.Add(new SwarmPermissionUpdateData { ToolName = tool, Action = PermissionBehavior.Deny });
            }
        }

        return updates;
    }

    private static bool IsAutoApprovedTool(string toolName) {
        var autoApproved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "read_file", "list_files", "search_files", "get_file_info",
            "code_search", "symbol_search", "grep", "glob",
            "agent_list", "agent_status", "agent_get_messages"
        };

        return autoApproved.Contains(toolName);
    }

    private static bool IsDangerousTool(string toolName) {
        var dangerous = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "file_delete", "rm", "delete",
            "git_reset", "git_clean", "git_push",
            "format_disk", "shutdown"
        };

        return dangerous.Contains(toolName);
    }
}