
namespace McpClient;

/// <summary>
/// MCP WebSocket 客户端 — 基于 WebSocket 传输与 MCP 服务器通信,适用于长连接双向通信场景。
/// </summary>
public sealed partial class McpWebSocketClient : McpNetworkClient<Transports.WebSocketTransport>
{
    /// <summary>传输类型名称,用于日志与事件标识。</summary>
    protected override string TransportTypeName => "websocket";

    /// <summary>
    /// 构造 McpWebSocketClient 实例。
    /// </summary>
    /// <param name="config">服务器连接配置。</param>
    /// <param name="options">客户端选项,为 null 时使用默认值。</param>
    /// <param name="logger">日志记录器。</param>
    /// <param name="authProvider">认证提供者,为 null 且配置含 Auth 时自动创建。</param>
    public McpWebSocketClient(McpServerConnectionConfig config, McpClientOptions? options = null, ILogger? logger = null, IMcpAuthProvider? authProvider = null)
        : base(config, options, logger, authProvider,
            CreateTransport(config, authProvider, logger))
    {
    }

    private static Transports.WebSocketTransport CreateTransport(
        McpServerConnectionConfig config, IMcpAuthProvider? authProvider, ILogger? logger)
    {
        IMcpAuthProvider? resolvedAuthProvider = authProvider;
        if (resolvedAuthProvider == null && config.Auth != null)
        {
            resolvedAuthProvider = McpAuthProviderFactory.Create(config.Auth, logger);
        }

        return new Transports.WebSocketTransport(config, resolvedAuthProvider, logger as ILogger<Transports.WebSocketTransport>);
    }
}
