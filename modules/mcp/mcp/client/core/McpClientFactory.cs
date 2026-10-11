
namespace McpClient;

/// <summary>
/// MCP 客户端工厂 — 根据连接配置创建对应传输类型的 MCP 客户端实例,支持回退链构建。
/// </summary>
[Register(typeof(IMcpClientFactory), ServiceLifetime.Singleton)]
public sealed partial class McpClientFactory : ServiceEntity, IMcpClientFactory {
    /// <summary>
    /// 根据连接配置创建 MCP 客户端实例 — 按 TransportType 选择 Stdio/Http/WebSocket 客户端。
    /// </summary>
    /// <param name="config">服务器连接配置。</param>
    /// <param name="logger">日志记录器。</param>
    /// <returns>对应传输类型的 IMcpClient 实例。</returns>
    public IMcpClient CreateClient(McpServerConnectionConfig config, ILogger? logger = null) {
        ArgumentNullException.ThrowIfNull(config);

        return config.TransportType switch {
            McpClientTransportType.Stdio => new McpStdioClient(config, logger: logger),
            McpClientTransportType.Http => new McpHttpClient(config, logger: logger),
            McpClientTransportType.WebSocket => new McpWebSocketClient(config, logger: logger),
            _ => throw new NotSupportedException($"[MCP021] 不支持的传输类型: {config.TransportType}")
        };
    }

    /// <summary>
    /// 根据连接配置创建 MCP 客户端实例 — 可选启用回退链。
    /// </summary>
    /// <param name="config">服务器连接配置。</param>
    /// <param name="enableFallback">是否启用回退链,true 时创建带回退的客户端。</param>
    /// <param name="logger">日志记录器。</param>
    /// <returns>对应传输类型的 IMcpClient 实例。</returns>
    public IMcpClient CreateClient(McpServerConnectionConfig config, bool enableFallback, ILogger? logger = null) {
        return enableFallback ? CreateClientWithFallback(config, logger: logger) : CreateClient(config, logger);
    }

    /// <summary>
    /// 创建带传输回退链的 MCP 客户端 — 主传输故障时自动切换到健康备用传输。
    /// </summary>
    /// <param name="config">服务器连接配置。</param>
    /// <param name="fallbackConfig">回退策略配置,为 null 时从环境变量读取。</param>
    /// <param name="logger">日志记录器。</param>
    /// <returns>带回退链的 McpFallbackClient 实例。</returns>
    public IMcpClient CreateClientWithFallback(
        McpServerConnectionConfig config,
        TransportFallbackConfig? fallbackConfig = null,
        ILogger? logger = null) {
        ArgumentNullException.ThrowIfNull(config);
        fallbackConfig ??= TransportFallbackConfig.FromEnvironment();

        var clientChain = BuildClientFallbackChain(config, logger);
        return new McpFallbackClient(config, clientChain, fallbackConfig, logger);
    }

    private static (IMcpTransport[] Transports, ITransportHealthCheck[] HealthChecks) BuildClientFallbackChain(
        McpServerConnectionConfig config, ILogger? logger) {
        var transports = new List<IMcpTransport>();
        var healthChecks = new List<ITransportHealthCheck>();

        if (config.TransportType == McpClientTransportType.Stdio && !string.IsNullOrWhiteSpace(config.Endpoint)) {
            healthChecks.Add(new StdioHealthCheck(config.Endpoint, new IO.FileSystem.PhysicalFileSystem()));
        }

        if (!string.IsNullOrWhiteSpace(config.Endpoint) && config.TransportType != McpClientTransportType.Stdio) {
            transports.Add(new HttpTransport(config, logger: logger as ILogger<HttpTransport>));
            healthChecks.Add(new HttpListenerHealthCheck($"http://localhost:{ExtractPort(config.Endpoint)}/"));

            transports.Add(new WebSocketTransport(config));
        }

        if (transports.Count == 0)
            throw new InvalidOperationException($"Cannot build fallback chain: no transports available for config '{config.Name}'");

        return (transports.ToArray(), healthChecks.ToArray());
    }

    private static int ExtractPort(string endpoint) {
        try {
            var uri = new Uri(endpoint);
            return uri.Port > 0 ? uri.Port : 80;
        } catch {
            return 80;
        }
    }
}