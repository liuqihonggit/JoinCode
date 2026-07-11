namespace McpClient.Mcpb;

/// <summary>
/// MCPB 缓存检查中间件 — 检查解压目录是否存在且未过期，命中时短路
/// </summary>
[Register(typeof(IMcpbMiddleware))]
public sealed partial class McpbCacheCheckMiddleware : IMcpbMiddleware
{
    [Inject] private readonly IFileSystem _fs;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(McpbLoadContext context, MiddlewareDelegate<McpbLoadContext> next, CancellationToken ct)
    {
        var extractPath = context.ExtractPath;
        var mcpbPath = context.LocalFilePath;

        if (_fs.DirectoryExists(extractPath) && !await IsCacheStaleAsync(mcpbPath, extractPath, ct).ConfigureAwait(false))
        {
            context.IsCacheHit = true;
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task<bool> IsCacheStaleAsync(string mcpbPath, string extractPath, CancellationToken ct)
    {
        var metadataPath = Path.Combine(extractPath, ".mcpb-metadata.json");
        if (!_fs.FileExists(metadataPath))
        {
            return true;
        }

        try
        {
            var json = await _fs.ReadAllTextAsync(metadataPath, ct).ConfigureAwait(false);
            var metadata = JsonSerializer.Deserialize(json, McpClientJsonContext.Default.McpbCacheMetadata);
            if (metadata == null) return true;

            var lastWrite = _fs.GetLastWriteTimeUtc(mcpbPath);
            return lastWrite > metadata.CachedAt;
        }
        catch
        {
            return true;
        }
    }
}
