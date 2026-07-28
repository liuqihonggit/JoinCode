namespace McpClient.Mcpb;

/// <summary>
/// MCPB 哈希计算中间件 — 计算文件内容哈希，确定解压目标路径
/// </summary>
[Register(typeof(IMcpbMiddleware))]
public sealed partial class McpbHashMiddleware : IMcpbMiddleware
{
    [Inject] private readonly IFileSystem _fs;


    public async Task InvokeAsync(McpbLoadContext context, MiddlewareDelegate<McpbLoadContext> next, CancellationToken ct)
    {
        var filePath = context.LocalFilePath;

        using var stream = _fs.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        context.ContentHash = Convert.ToHexString(hash).AsSpan(0, 16).ToString().ToLowerInvariant();
        context.ExtractPath = Path.Combine(context.ExtractBasePath, context.ContentHash);

        await next(context, ct).ConfigureAwait(false);
    }
}
