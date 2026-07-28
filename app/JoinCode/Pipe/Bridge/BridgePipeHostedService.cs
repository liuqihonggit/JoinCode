namespace JoinCode.Pipe;

public sealed partial class BridgePipeHostedService : IHostedService, IAsyncDisposable
{
    private readonly BridgeHeartbeatService _heartbeatService;
    private readonly IPipeRouteRegistrar? _routeRegistrar;
    private readonly Core.Bridge.BridgeServer? _bridgeServer;
    [Inject] private readonly ILogger<BridgePipeHostedService>? _logger;
    private readonly CancellationTokenSource _disposeCts = new();

    public BridgePipeHostedService(
        BridgeHeartbeatService heartbeatService,
        IPipeRouteRegistrar? routeRegistrar = null,
        Core.Bridge.BridgeServer? bridgeServer = null,
        ILogger<BridgePipeHostedService>? logger = null)
    {
        _heartbeatService = heartbeatService;
        _routeRegistrar = routeRegistrar;
        _bridgeServer = bridgeServer;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("[BridgePipeHostedService] Bridge Pipe 服务正在启动...");

        if (_routeRegistrar is not null && _bridgeServer is not null)
        {
            _routeRegistrar.RegisterRoutes(_bridgeServer);
        }

        _heartbeatService.Start();
        _logger?.LogInformation("[BridgePipeHostedService] Bridge Pipe 服务已启动，心跳检测已开启");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("[BridgePipeHostedService] 正在停止 Bridge Pipe 服务...");
        _disposeCts.Cancel();
        _heartbeatService.Stop();
        _logger?.LogInformation("[BridgePipeHostedService] Bridge Pipe 服务已停止");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _heartbeatService.Stop();
        _disposeCts.Dispose();
        return ValueTask.CompletedTask;
    }
}
