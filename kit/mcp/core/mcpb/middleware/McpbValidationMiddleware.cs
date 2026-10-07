namespace McpClient.Mcpb;

/// <summary>
/// MCPB 参数验证中间件 — 检查源路径有效性，URL 源时下载到临时文件
/// </summary>
[Register(typeof(IMcpbMiddleware), ServiceLifetime.Singleton)]
public sealed partial class McpbValidationMiddleware : ServiceEntity, IMcpbMiddleware {

    /// <summary>
    /// 初始化 MCPB 参数验证中间件
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="downloader">多线程分片下载器(URL 源下载用)</param>
    /// <param name="logger">日志记录器（可选）</param>
    public McpbValidationMiddleware(IFileSystem fs, IDownloader downloader, ILogger<McpbValidationMiddleware>? logger = null) {
        _fs = fs;
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _logger = logger;
    }
    private readonly IFileSystem _fs;
    private readonly IDownloader _downloader;
    private readonly ILogger<McpbValidationMiddleware>? _logger;


    /// <summary>
    /// 执行验证中间件：检查源路径有效性，URL 源时下载到临时文件，完成后清理
    /// </summary>
    /// <param name="context">MCPB 加载上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(McpbLoadContext context, MiddlewareDelegate<McpbLoadContext> next, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(context.Source)) {
            context.Fail("MCPB 源路径不能为空");
            return;
        }

        if (string.IsNullOrWhiteSpace(context.ExtractBasePath)) {
            context.Fail("解压目标路径不能为空");
            return;
        }

        if (context.IsUrlSource) {
            _logger?.LogInformation("下载 MCPB: {Url}", context.Source);

            var tempPath = Path.Combine(Path.GetTempPath(), $"mcpb-{Guid.NewGuid():N}.mcpb");
            context.TempFilePath = tempPath;

            try {
                var options = new DownloadOptions { MaxThreads = 4, Resume = true };
                await using var session = _downloader.StartDownload(context.Source, tempPath, options, null, ct);
                var result = await session.WaitForCompletionAsync(ct).ConfigureAwait(false);
                if (!result.Success)
                    throw new InvalidOperationException($"MCPB 下载失败: {result.ErrorMessage}");

                context.LocalFilePath = tempPath;
            } catch {
                CleanupTempFile(context);
                throw;
            }
        } else {
            if (!_fs.FileExists(context.Source)) {
                context.Fail($"MCPB 文件不存在: {context.Source}");
                return;
            }

            context.LocalFilePath = context.Source;
        }

        try {
            await next(context, ct).ConfigureAwait(false);
        } finally {
            CleanupTempFile(context);
        }
    }

    private void CleanupTempFile(McpbLoadContext context) {
        if (context.TempFilePath != null && _fs.FileExists(context.TempFilePath)) {
            try { _fs.DeleteFile(context.TempFilePath); } catch (Exception ex) { _logger?.LogDebug(ex, "MCPB 下载后清理临时文件失败: {Path}", context.TempFilePath); }
        }
    }
}