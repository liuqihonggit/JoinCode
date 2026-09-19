
namespace Core.Agents.Coordinator;

/// <summary>Swarm 权限消息路由器 — 在邮箱与权限回调服务/请求处理器之间路由权限消息，驱动权限审批流程</summary>
[Register(typeof(SwarmPermissionMessageRouter), ServiceLifetime.Singleton)]
public sealed partial class SwarmPermissionMessageRouter : ServiceEntity {
    private readonly IMailbox _messageBroker;
    private readonly SwarmPermissionCallbackService _callbackService;
    private readonly ISwarmPermissionRequestProcessor _requestProcessor;
    private readonly ILogger<SwarmPermissionMessageRouter>? _logger;
    private CancellationTokenSource? _cts;
    private Task? _routingTask;

    /// <summary>
    /// 初始化 Swarm 权限消息路由器
    /// </summary>
    /// <param name="messageBroker">消息邮箱</param>
    /// <param name="callbackService">权限回调服务</param>
    /// <param name="requestProcessor">权限请求处理器</param>
    /// <param name="logger">日志记录器</param>
    public SwarmPermissionMessageRouter(
        IMailbox messageBroker,
        SwarmPermissionCallbackService callbackService,
        ISwarmPermissionRequestProcessor requestProcessor,
        ILogger<SwarmPermissionMessageRouter>? logger = null) {
        _messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
        _callbackService = callbackService ?? throw new ArgumentNullException(nameof(callbackService));
        _requestProcessor = requestProcessor ?? throw new ArgumentNullException(nameof(requestProcessor));
        _logger = logger;
    }

    /// <summary>
    /// 启动 Leader 侧消息路由，监听权限请求
    /// </summary>
    /// <param name="coordinatorAgentId">协调器智能体标识</param>
    public void StartRouting(string coordinatorAgentId) {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        _routingTask = RouteMessagesAsync(coordinatorAgentId, _cts.Token);

        _logger?.LogInformation("Swarm 权限消息路由已启动: CoordinatorId={CoordinatorId}", coordinatorAgentId);
    }

    /// <summary>
    /// 异步停止消息路由并释放相关资源
    /// </summary>
    /// <returns>表示异步操作的任务</returns>
    public async Task StopRoutingAsync() {
        if (_cts == null) return;

        _cts.Cancel();
        if (_routingTask != null) {
            try {
                await _routingTask.ConfigureAwait(false);
            } catch (OperationCanceledException) {
            }
        }

        _cts.Dispose();
        _cts = null;
        _routingTask = null;

        _logger?.LogInformation("Swarm 权限消息路由已停止");
    }

    /// <summary>
    /// 启动 Worker 侧响应路由，监听权限响应消息
    /// </summary>
    /// <param name="workerAgentId">Worker 智能体标识</param>
    public void StartWorkerResponseRouting(string workerAgentId) {
        _ = RouteWorkerResponsesAsync(workerAgentId);
    }

    private async Task RouteMessagesAsync(string coordinatorAgentId, CancellationToken ct) {
        try {
            await foreach (var message in _messageBroker.ReceiveAsync(coordinatorAgentId, ct).ConfigureAwait(false)) {
                if (ct.IsCancellationRequested) break;

                if (message.MessageType == SwarmPermissionMessageType.PermissionRequest.ToValue()) {
                    _ = ProcessRequestAsync(message, ct).WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                }
            }
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            _logger?.LogError(ex, "Swarm 权限消息路由异常退出: CoordinatorId={CoordinatorId}", coordinatorAgentId);
        }
    }

    private async Task RouteWorkerResponsesAsync(string workerAgentId) {
        try {
            await foreach (var message in _messageBroker.ReceiveAsync(workerAgentId).ConfigureAwait(false)) {
                if (message.MessageType == SwarmPermissionMessageType.PermissionResponse.ToValue()) {
                    await _callbackService.ProcessIncomingResponseMessageAsync(message).ConfigureAwait(false);
                }
            }
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            _logger?.LogError(ex, "Worker 权限响应路由异常退出: WorkerId={WorkerId}", workerAgentId);
        }
    }

    private async Task ProcessRequestAsync(CoordinatorAgentMessage message, CancellationToken ct) {
        try {
            var data = RelaxedJsonSerializer.Deserialize(
                message.Content,
                AgentsJsonContext.Default.SwarmPermissionRequestData);

            if (data == null) {
                _logger?.LogWarning("无法反序列化权限请求: From={FromId}", message.FromAgentId);
                return;
            }

            await _requestProcessor.ProcessRequestAsync(data, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogError(ex, "处理权限请求失败: From={FromId}", message.FromAgentId);
        }
    }
}