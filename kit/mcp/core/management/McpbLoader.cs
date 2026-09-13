
namespace McpClient;

/// <summary>
/// MCPB 包加载器 — 通过管道中间件加载 .mcpb/.dxt 包
/// 管道: 验证 → 哈希 → 缓存检查 → 解压 → 解析清单
/// </summary>
[Register(typeof(McpbLoader), ServiceLifetime.Singleton)]
public sealed partial class McpbLoader : ServiceEntity
{
    private readonly MiddlewarePipeline<McpbLoadContext> _pipeline;

    /// <summary>
    /// 初始化 MCPB 包加载器，按中间件顺序构建加载管道
    /// </summary>
    /// <param name="middlewares">中间件集合（按注入顺序执行）</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="loggerFactory">日志工厂（可选，传入则启用管道日志）</param>
    /// <param name="logger">日志记录器（可选）</param>
    public McpbLoader(
        IEnumerable<IMcpbMiddleware> middlewares,
        IFileSystem fs,
        ILoggerFactory? loggerFactory = null,
        ILogger<McpbLoader>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        _pipeline = loggerFactory is not null
            ? new PipelineBuilder<McpbLoadContext>()
                .WithLoggingScope(loggerFactory)
                .UseRange(middlewares)
                .Build()
            : new MiddlewarePipeline<McpbLoadContext>(middlewares);
    }

    /// <summary>
    /// 判断指定源是否为 MCPB 包（.mcpb 或 .dxt 后缀）
    /// </summary>
    /// <param name="source">源路径或 URL</param>
    /// <returns>若为 MCPB 包返回 true；否则 false</returns>
    public static bool IsMcpbSource(string source)
    {
        return source.EndsWith(".mcpb", StringComparison.OrdinalIgnoreCase)
            || source.EndsWith(".dxt", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 异步从本地路径加载 MCPB 包
    /// </summary>
    /// <param name="mcpbPath">MCPB 包本地路径</param>
    /// <param name="extractBasePath">解压目标基础路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>MCPB 加载结果</returns>
    /// <exception cref="InvalidOperationException">加载失败或未产生结果时抛出</exception>
    public async Task<McpbLoadResult> LoadFromLocalAsync(string mcpbPath, string extractBasePath, CancellationToken cancellationToken = default)
    {
        var context = new McpbLoadContext
        {
            Source = mcpbPath,
            ExtractBasePath = extractBasePath,
            IsUrlSource = false,
            CancellationToken = cancellationToken
        };

        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        if (context.Failed)
            throw new InvalidOperationException(context.ErrorMessage);

        return context.Result ?? throw new InvalidOperationException("[MPB004] MCPB 加载未产生结果");
    }

    /// <summary>
    /// 异步从 URL 下载并加载 MCPB 包
    /// </summary>
    /// <param name="url">MCPB 包 URL</param>
    /// <param name="extractBasePath">解压目标基础路径</param>
    /// <param name="httpClient">用于下载的 HTTP 客户端</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>MCPB 加载结果</returns>
    /// <exception cref="InvalidOperationException">加载失败或未产生结果时抛出</exception>
    public async Task<McpbLoadResult> LoadFromUrlAsync(string url, string extractBasePath, HttpClient httpClient, CancellationToken cancellationToken = default)
    {
        var context = new McpbLoadContext
        {
            Source = url,
            ExtractBasePath = extractBasePath,
            IsUrlSource = true,
            HttpClient = httpClient,
            CancellationToken = cancellationToken
        };

        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        if (context.Failed)
            throw new InvalidOperationException(context.ErrorMessage);

        return context.Result ?? throw new InvalidOperationException("[MPB004] MCPB 加载未产生结果");
    }

    /// <summary>
    /// 根据清单生成 MCP 服务器连接配置，注入 EXTENSION_PATH 等环境变量
    /// </summary>
    /// <param name="manifest">MCPB 清单</param>
    /// <param name="extractedPath">解压后的插件路径</param>
    /// <param name="userConfig">用户自定义配置（可选，键名大写后加 USER_CONFIG_ 前缀注入环境变量）</param>
    /// <returns>MCP 服务器连接配置；清单缺少 Server 时抛出异常</returns>
    public McpServerConnectionConfig? GenerateMcpConfig(McpbManifest manifest, string extractedPath, Dictionary<string, string>? userConfig = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifest.Server);

        var server = manifest.Server;
        var transportType = server.Type?.ToLowerInvariant() switch
        {
            "sse" or "http" or "streamable-http" => McpClientTransportType.Http,
            "websocket" or "ws" => McpClientTransportType.WebSocket,
            _ => McpClientTransportType.Stdio
        };

        var env = new Dictionary<string, string>();

        if (server.Env.Count > 0)
        {
            foreach (var kvp in server.Env)
            {
                if (kvp.Value.ValueKind == JsonValueKind.String)
                {
                    env[kvp.Key] = kvp.Value.GetString()?.Replace("${EXTENSION_PATH}", extractedPath) ?? string.Empty;
                }
            }
        }

        env[ClaudeCompatConstants.EnvPluginRoot] = extractedPath;
        env[ClaudeCompatConstants.EnvPluginData] = Path.Combine(extractedPath, ".data");

        if (userConfig != null)
        {
            foreach (var kvp in userConfig)
            {
                env[$"USER_CONFIG_{kvp.Key.ToUpperInvariant()}"] = kvp.Value;
            }
        }

        return new McpServerConnectionConfig
        {
            Name = manifest.Name ?? "unknown",
            TransportType = transportType,
            Environment = env,
            Endpoint = transportType == McpClientTransportType.Stdio
                ? server.Command ?? "node"
                : server.Url ?? string.Empty
        };
    }
}
