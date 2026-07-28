
namespace Core.Agents.Coordinator;

[Register]
public sealed partial class SwarmPermissionMessageRouter
{
    private readonly IAgentMessageBroker _messageBroker;
    private readonly SwarmPermissionCallbackService _callbackService;
    private readonly ISwarmPermissionRequestProcessor _requestProcessor;
    [Inject] private readonly ILogger<SwarmPermissionMessageRouter>? _logger;
    private CancellationTokenSource? _cts;
    private Task? _routingTask;

    public SwarmPermissionMessageRouter(
        IAgentMessageBroker messageBroker,
        SwarmPermissionCallbackService callbackService,
        ISwarmPermissionRequestProcessor requestProcessor,
        ILogger<SwarmPermissionMessageRouter>? logger = null)
    {
        _messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
        _callbackService = callbackService ?? throw new ArgumentNullException(nameof(callbackService));
        _requestProcessor = requestProcessor ?? throw new ArgumentNullException(nameof(requestProcessor));
        _logger = logger;
    }

    public void StartRouting(string coordinatorAgentId)
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        _routingTask = RouteMessagesAsync(coordinatorAgentId, _cts.Token);

        _logger?.LogInformation("Swarm 权限消息路由已启动: CoordinatorId={CoordinatorId}", coordinatorAgentId);
    }

    public async Task StopRoutingAsync()
    {
        if (_cts == null) return;

        _cts.Cancel();
        if (_routingTask != null)
        {
            try
            {
                await _routingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
        _cts = null;
        _routingTask = null;

        _logger?.LogInformation("Swarm 权限消息路由已停止");
    }

    public void StartWorkerResponseRouting(string workerAgentId)
    {
        _ = RouteWorkerResponsesAsync(workerAgentId);
    }

    private async Task RouteMessagesAsync(string coordinatorAgentId, CancellationToken ct)
    {
        try
        {
            await foreach (var message in _messageBroker.ReadMessagesAsync(coordinatorAgentId, ct).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested) break;

                if (message.MessageType == SwarmPermissionMessageType.PermissionRequest.ToValue())
                {
                    _ = ProcessRequestAsync(message, ct).WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Swarm 权限消息路由异常退出: CoordinatorId={CoordinatorId}", coordinatorAgentId);
        }
    }

    private async Task RouteWorkerResponsesAsync(string workerAgentId)
    {
        try
        {
            await foreach (var message in _messageBroker.ReadMessagesAsync(workerAgentId).ConfigureAwait(false))
            {
                if (message.MessageType == SwarmPermissionMessageType.PermissionResponse.ToValue())
                {
                    await _callbackService.ProcessIncomingResponseMessageAsync(message).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Worker 权限响应路由异常退出: WorkerId={WorkerId}", workerAgentId);
        }
    }

    private async Task ProcessRequestAsync(CoordinatorAgentMessage message, CancellationToken ct)
    {
        try
        {
            var data = JsonSerializer.Deserialize(
                message.Content,
                AgentsJsonContext.Default.SwarmPermissionRequestData);

            if (data == null)
            {
                _logger?.LogWarning("无法反序列化权限请求: From={FromId}", message.FromAgentId);
                return;
            }

            await _requestProcessor.ProcessRequestAsync(data, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理权限请求失败: From={FromId}", message.FromAgentId);
        }
    }
}
