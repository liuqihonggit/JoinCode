
namespace Core.Agents.Coordinator;

/// <summary>
/// Plan 审批消息路由器 — 对齐 TS plan_approval_request/response 消息分发
/// Leader 侧: 检测 plan_approval_request → 自动批准 → 发送 plan_approval_response
/// Teammate 侧: 检测 plan_approval_response → 调用 IPlanModeManager.HandlePlanApprovalResponseAsync
/// </summary>
[Register]
public sealed partial class PlanApprovalMessageRouter
{
    private readonly IAgentMessageBroker _messageBroker;
    private readonly IPlanModeManager _planModeManager;
    private readonly IToolPermissionManager? _permissionManager;
    [Inject] private readonly ILogger<PlanApprovalMessageRouter>? _logger;
    [Inject] private readonly IClockService _clock;
    private CancellationTokenSource? _leaderCts;
    private Task? _leaderRoutingTask;

    public PlanApprovalMessageRouter(
        IAgentMessageBroker messageBroker,
        IPlanModeManager planModeManager,
        IToolPermissionManager? permissionManager = null,
        ILogger<PlanApprovalMessageRouter>? logger = null,
        IClockService? clock = null)
    {
        _messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
        _planModeManager = planModeManager ?? throw new ArgumentNullException(nameof(planModeManager));
        _permissionManager = permissionManager;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 启动 Leader 侧路由：监听 plan_approval_request 并自动批准
    /// </summary>
    public void StartLeaderRouting(string coordinatorAgentId)
    {
        if (_leaderCts != null) return;

        _leaderCts = new CancellationTokenSource();
        _leaderRoutingTask = RouteLeaderMessagesAsync(coordinatorAgentId, _leaderCts.Token);

        _logger?.LogInformation("Plan 审批消息路由已启动: CoordinatorId={CoordinatorId}", coordinatorAgentId);
    }

    /// <summary>
    /// 启动 Teammate 侧路由：监听 plan_approval_response 并调用 HandlePlanApprovalResponseAsync
    /// </summary>
    public void StartTeammateRouting(string teammateAgentId)
    {
        _ = RouteTeammateResponsesAsync(teammateAgentId);
    }

    /// <summary>
    /// 停止 Leader 侧路由
    /// </summary>
    public async Task StopRoutingAsync()
    {
        if (_leaderCts == null) return;

        _leaderCts.Cancel();
        if (_leaderRoutingTask != null)
        {
            try
            {
                await _leaderRoutingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _leaderCts.Dispose();
        _leaderCts = null;
        _leaderRoutingTask = null;

        _logger?.LogInformation("Plan 审批消息路由已停止");
    }

    /// <summary>
    /// Leader 侧消息路由：监听 plan_approval_request 并自动批准
    /// 对齐 TS: Leader 收到 teammate 的 plan_approval_request 后自动批准
    /// </summary>
    private async Task RouteLeaderMessagesAsync(string coordinatorAgentId, CancellationToken ct)
    {
        try
        {
            await foreach (var message in _messageBroker.ReadMessagesAsync(coordinatorAgentId, ct).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested) break;

                if (message.MessageType == TeammateMessageType.PlanApprovalRequest.ToValue())
                {
                    _ = ProcessPlanApprovalRequestAsync(message, ct).WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Plan 审批消息路由异常退出: CoordinatorId={CoordinatorId}", coordinatorAgentId);
        }
    }

    /// <summary>
    /// Teammate 侧响应路由：监听 plan_approval_response 并调用 HandlePlanApprovalResponseAsync
    /// </summary>
    private async Task RouteTeammateResponsesAsync(string teammateAgentId)
    {
        try
        {
            await foreach (var message in _messageBroker.ReadMessagesAsync(teammateAgentId).ConfigureAwait(false))
            {
                if (message.MessageType == TeammateMessageType.PlanApprovalResponse.ToValue())
                {
                    await ProcessPlanApprovalResponseAsync(message).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Teammate Plan 审批响应路由异常退出: TeammateId={TeammateId}", teammateAgentId);
        }
    }

    /// <summary>
    /// 处理 plan_approval_request — Leader 侧自动批准
    /// 对齐 TS: Leader 自动批准 teammate 的 plan 退出请求
    /// </summary>
    private async Task ProcessPlanApprovalRequestAsync(CoordinatorAgentMessage message, CancellationToken ct)
    {
        try
        {
            var request = JsonSerializer.Deserialize(
                message.Content,
                AgentsJsonContext.Default.PlanApprovalRequestMessage);

            if (request == null)
            {
                _logger?.LogWarning("无法反序列化 Plan 审批请求: From={FromId}", message.FromAgentId);
                return;
            }

            _logger?.LogInformation(
                "收到 Plan 审批请求: RequestId={RequestId}, From={From}, PlanFile={PlanFile}",
                request.RequestId, request.From, request.PlanFilePath);

            // 对齐 TS: Leader 自动批准 — 构造 plan_approval_response
            var currentMode = _permissionManager != null
                ? await _permissionManager.GetCurrentModeAsync(ct).ConfigureAwait(false)
                : PermissionMode.Default;

            var response = new PlanApprovalResponseMessage
            {
                From = "team-lead",
                Timestamp = _clock.GetUtcNow().ToString("o"),
                RequestId = request.RequestId,
                Approved = true,
                PermissionMode = currentMode.ToValue()
            };

            var responseContent = JsonSerializer.Serialize(response, AgentsJsonContext.Default.PlanApprovalResponseMessage);

            // 通过 broker 发送审批响应给 teammate
            var responseMessage = new CoordinatorAgentMessage
            {
                FromAgentId = "team-lead",
                ToAgentId = message.FromAgentId,
                MessageType = TeammateMessageType.PlanApprovalResponse.ToValue(),
                Content = responseContent
            };

            await _messageBroker.SendMessageAsync(message.FromAgentId, responseMessage, ct).ConfigureAwait(false);

            _logger?.LogInformation(
                "Plan 审批已自动批准: RequestId={RequestId}, To={ToId}, PermissionMode={Mode}",
                request.RequestId, message.FromAgentId, currentMode.ToValue());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理 Plan 审批请求失败: From={FromId}", message.FromAgentId);
        }
    }

    /// <summary>
    /// 处理 plan_approval_response — Teammate 侧接收审批结果
    /// 对齐 TS: Teammate 收到 Leader 的审批响应后恢复权限模式
    /// </summary>
    private async Task ProcessPlanApprovalResponseAsync(CoordinatorAgentMessage message)
    {
        try
        {
            var response = JsonSerializer.Deserialize(
                message.Content,
                AgentsJsonContext.Default.PlanApprovalResponseMessage);

            if (response == null)
            {
                _logger?.LogWarning("无法反序列化 Plan 审批响应: From={FromId}", message.FromAgentId);
                return;
            }

            _logger?.LogInformation(
                "收到 Plan 审批响应: RequestId={RequestId}, Approved={Approved}, From={From}",
                response.RequestId, response.Approved, response.From);

            await _planModeManager.HandlePlanApprovalResponseAsync(response).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理 Plan 审批响应失败: From={FromId}", message.FromAgentId);
        }
    }
}
