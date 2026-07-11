namespace McpClient.Mcpb;

/// <summary>
/// MCPB 参数验证中间件 — 检查源路径有效性，URL 源时下载到临时文件
/// </summary>
[Register(typeof(IMcpbMiddleware))]
public sealed partial class McpbValidationMiddleware : IMcpbMiddleware
{
    [Inject] private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<McpbValidationMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(McpbLoadContext context, MiddlewareDelegate<McpbLoadContext> next, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.Source))
        {
            context.Fail("MCPB 源路径不能为空");
            return;
        }

        if (string.IsNullOrWhiteSpace(context.ExtractBasePath))
        {
            context.Fail("解压目标路径不能为空");
            return;
        }

        if (context.IsUrlSource)
        {
            if (context.HttpClient == null)
            {
                context.Fail("URL 源需要 HttpClient");
                return;
            }

            _logger?.LogInformation("下载 MCPB: {Url}", context.Source);

            var tempPath = Path.Combine(Path.GetTempPath(), $"mcpb-{Guid.NewGuid():N}.mcpb");
            context.TempFilePath = tempPath;

            try
            {
                using var response = await context.HttpClient.GetAsync(context.Source, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var fileStream = _fs.CreateStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream, ct).ConfigureAwait(false);

                context.LocalFilePath = tempPath;
            }
            catch
            {
                CleanupTempFile(context);
                throw;
            }
        }
        else
        {
            if (!_fs.FileExists(context.Source))
            {
                context.Fail($"MCPB 文件不存在: {context.Source}");
                return;
            }

            context.LocalFilePath = context.Source;
        }

        try
        {
            await next(context, ct).ConfigureAwait(false);
        }
        finally
        {
            CleanupTempFile(context);
        }
    }

    private void CleanupTempFile(McpbLoadContext context)
    {
        if (context.TempFilePath != null && _fs.FileExists(context.TempFilePath))
        {
            try { _fs.DeleteFile(context.TempFilePath); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"McpbLoader: Failed to delete temp file: {ex.Message}"); }
        }
    }
}
