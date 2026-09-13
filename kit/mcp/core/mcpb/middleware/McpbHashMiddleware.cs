namespace McpClient.Mcpb;

/// <summary>
/// MCPB 哈希计算中间件 — 计算文件内容哈希，确定解压目标路径
/// </summary>
[Register(typeof(IMcpbMiddleware), ServiceLifetime.Singleton)]
public sealed partial class McpbHashMiddleware : ServiceEntity, IMcpbMiddleware
{

    /// <summary>
    /// 初始化 MCPB 哈希计算中间件
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    public McpbHashMiddleware(IFileSystem fs)
    {
        _fs = fs;
    }
    private readonly IFileSystem _fs;


    /// <summary>
    /// 执行哈希计算中间件：计算文件 SHA256 哈希（取前 16 位十六进制），确定解压目标路径
    /// </summary>
    /// <param name="context">MCPB 加载上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
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
