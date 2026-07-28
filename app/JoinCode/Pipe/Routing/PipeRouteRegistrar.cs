
namespace JoinCode.Pipe;

[Register]
public sealed partial class PipeRouteRegistrar : IPipeRouteRegistrar
{
    private readonly CodeSessionApiHandler _codeSessionHandler;
    [Inject] private readonly ILogger<PipeRouteRegistrar>? _logger;

    public PipeRouteRegistrar(CodeSessionApiHandler codeSessionHandler, ILogger<PipeRouteRegistrar>? logger = null)
    {
        _codeSessionHandler = codeSessionHandler;
        _logger = logger;
    }

    public void RegisterRoutes(Core.Bridge.BridgeServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        server.RegisterRoute("/code-sessions", _codeSessionHandler.HandleHttpRequestAsync);
        server.RegisterRoute("/code-sessions/", _codeSessionHandler.HandleHttpRequestAsync);

        _logger?.LogInformation("[PipeRouteRegistrar] Code Session API 路由已注册到 BridgeServer");
    }
}
