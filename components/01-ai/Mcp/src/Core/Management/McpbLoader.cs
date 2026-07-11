
namespace McpClient;

/// <summary>
/// MCPB 包加载器 — 通过管道中间件加载 .mcpb/.dxt 包
/// 管道: 验证 → 哈希 → 缓存检查 → 解压 → 解析清单
/// </summary>
[Register]
public sealed partial class McpbLoader
{
    private readonly MiddlewarePipeline<McpbLoadContext> _pipeline;

    public McpbLoader(
        IEnumerable<IMcpbMiddleware> middlewares,
        IFileSystem fs,
        ILogger<McpbLoader>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        _pipeline = new MiddlewarePipeline<McpbLoadContext>(middlewares);
    }

    public static bool IsMcpbSource(string source)
    {
        return source.EndsWith(".mcpb", StringComparison.OrdinalIgnoreCase)
            || source.EndsWith(".dxt", StringComparison.OrdinalIgnoreCase);
    }

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

        return context.Result ?? throw new InvalidOperationException("MCPB 加载未产生结果");
    }

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

        return context.Result ?? throw new InvalidOperationException("MCPB 加载未产生结果");
    }

    public McpServerConnectionConfig? GenerateMcpConfig(McpbManifest manifest, string extractedPath, Dictionary<string, string>? userConfig = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifest.Server);

        var server = manifest.Server;
        var transportType = server.Type?.ToLowerInvariant() switch
        {
            "sse" => McpClientTransportType.Sse,
            "http" or "streamable-http" => McpClientTransportType.Http,
            "websocket" or "ws" => McpClientTransportType.WebSocket,
            _ => McpClientTransportType.Stdio
        };

        var env = new Dictionary<string, string>();

        if (server.Env != null)
        {
            foreach (var kvp in server.Env)
            {
                if (kvp.Value.ValueKind == JsonValueKind.String)
                {
                    env[kvp.Key] = kvp.Value.GetString()?.Replace("${EXTENSION_PATH}", extractedPath) ?? string.Empty;
                }
            }
        }

        env["CLAUDE_PLUGIN_ROOT"] = extractedPath;
        env["CLAUDE_PLUGIN_DATA"] = Path.Combine(extractedPath, ".data");

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
