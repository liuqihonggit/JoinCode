namespace McpClient.Mcpb;

/// <summary>
/// MCPB 清单解析中间件 — 解析 manifest.json 并构建最终结果
/// </summary>
[Register(typeof(IMcpbMiddleware))]
public sealed partial class McpbManifestMiddleware : IMcpbMiddleware
{
    [Inject] private readonly IFileSystem _fs;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(McpbLoadContext context, MiddlewareDelegate<McpbLoadContext> next, CancellationToken ct)
    {
        var extractPath = context.ExtractPath;
        var manifestPath = Path.Combine(extractPath, "manifest.json");

        if (!_fs.FileExists(manifestPath))
        {
            throw new InvalidOperationException("MCPB 缺少 manifest.json");
        }

        var json = await _fs.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize(json, McpClientJsonContext.Default.McpbManifest);

        if (manifest == null)
        {
            throw new InvalidOperationException("无法解析 MCPB manifest.json");
        }

        if (manifest.Server == null)
        {
            throw new InvalidOperationException("MCPB manifest 缺少 server 配置");
        }

        context.Manifest = manifest;
        context.Result = new McpbLoadResult
        {
            Manifest = manifest,
            ExtractedPath = extractPath,
            ContentHash = context.ContentHash
        };

        await next(context, ct).ConfigureAwait(false);
    }
}
