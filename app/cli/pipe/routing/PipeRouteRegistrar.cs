
namespace JoinCode.Pipe;

/// <summary>
/// 管道路由注册器 — 将 Code Session API 路由注册到 BridgeServer
/// </summary>
[Register(typeof(IPipeRouteRegistrar), ServiceLifetime.Singleton)]
public sealed partial class PipeRouteRegistrar : ServiceEntity, IPipeRouteRegistrar
{
    private readonly CodeSessionApiHandler _codeSessionHandler;
    private readonly ILogger<PipeRouteRegistrar>? _logger;

    /// <summary>构造函数</summary>
    /// <param name="codeSessionHandler">Code Session API 请求处理器</param>
    /// <param name="logger">日志记录器（可选）</param>
    public PipeRouteRegistrar(CodeSessionApiHandler codeSessionHandler, ILogger<PipeRouteRegistrar>? logger = null)
    {
        _codeSessionHandler = codeSessionHandler;
        _logger = logger;
    }

    /// <summary>将 Code Session API 路由注册到 BridgeServer</summary>
    /// <param name="server">Bridge 服务器实例</param>
    public void RegisterRoutes(Core.Bridge.BridgeServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        server.RegisterRoute("/code-sessions", _codeSessionHandler.HandleHttpRequestAsync);
        server.RegisterRoute("/code-sessions/", _codeSessionHandler.HandleHttpRequestAsync);

        _logger?.LogInformation("[PipeRouteRegistrar] Code Session API 路由已注册到 BridgeServer");
    }
}
