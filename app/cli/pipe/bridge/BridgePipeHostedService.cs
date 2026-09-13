namespace JoinCode.Pipe;

/// <summary>Bridge Pipe 宿主服务 — 启动时注册路由并开启心跳检测，停止时取消令牌并停止心跳</summary>
public sealed partial class BridgePipeHostedService : IHostedService, IAsyncDisposable
{
    private readonly BridgeHeartbeatService _heartbeatService;
    private readonly IPipeRouteRegistrar? _routeRegistrar;
    private readonly Core.Bridge.BridgeServer? _bridgeServer;
    private readonly ILogger<BridgePipeHostedService>? _logger;
    private readonly CancellationTokenSource _disposeCts = new();

    /// <summary>构造 Bridge Pipe 宿主服务</summary>
    /// <param name="heartbeatService">心跳检测服务</param>
    /// <param name="routeRegistrar">路由注册器；为 null 则跳过路由注册</param>
    /// <param name="bridgeServer">Bridge 服务端；为 null 则跳过路由注册</param>
    /// <param name="logger">日志记录器；为 null 则不记录日志</param>
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

    /// <summary>启动服务 — 注册路由并开启心跳检测</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步启动操作的任务</returns>
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

    /// <summary>停止服务 — 取消内部令牌并停止心跳检测</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步停止操作的任务</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("[BridgePipeHostedService] 正在停止 Bridge Pipe 服务...");
        _disposeCts.Cancel();
        _heartbeatService.Stop();
        _logger?.LogInformation("[BridgePipeHostedService] Bridge Pipe 服务已停止");
        return Task.CompletedTask;
    }

    /// <summary>异步释放资源 — 取消内部令牌、停止心跳并释放令牌源</summary>
    /// <returns>表示异步释放操作的任务</returns>
    public ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _heartbeatService.Stop();
        _disposeCts.Dispose();
        return ValueTask.CompletedTask;
    }
}
