namespace McpClient.Mcpb;

/// <summary>
/// MCPB 缓存检查中间件 — 检查解压目录是否存在且未过期，命中时短路
/// </summary>
[Register(typeof(IMcpbMiddleware), ServiceLifetime.Singleton)]
public sealed partial class McpbCacheCheckMiddleware : ServiceEntity, IMcpbMiddleware {

    /// <summary>
    /// 初始化 MCPB 缓存检查中间件
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    public McpbCacheCheckMiddleware(IFileSystem fs) {
        _fs = fs;
    }
    private readonly IFileSystem _fs;

    /// <summary>错误行为：发生错误时继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行缓存检查中间件：检查解压目录是否存在且未过期，命中时设置 IsCacheHit 标志
    /// </summary>
    /// <param name="context">MCPB 加载上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(McpbLoadContext context, MiddlewareDelegate<McpbLoadContext> next, CancellationToken ct) {
        var extractPath = context.ExtractPath;
        var mcpbPath = context.LocalFilePath;

        if (_fs.DirectoryExists(extractPath) && !await IsCacheStaleAsync(mcpbPath, extractPath, ct).ConfigureAwait(false)) {
            context.IsCacheHit = true;
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task<bool> IsCacheStaleAsync(string mcpbPath, string extractPath, CancellationToken ct) {
        var metadataPath = Path.Combine(extractPath, ".mcpb-metadata.json");
        if (!_fs.FileExists(metadataPath)) {
            return true;
        }

        try {
            var metadata = await _fs.ReadAndDeserializeAsync(metadataPath, McpClientJsonContext.Default.McpbCacheMetadata, ct).ConfigureAwait(false);
            if (metadata == null) return true;

            var lastWrite = _fs.GetLastWriteTimeUtc(mcpbPath);
            return lastWrite > metadata.CachedAt;
        } catch {
            return true;
        }
    }
}