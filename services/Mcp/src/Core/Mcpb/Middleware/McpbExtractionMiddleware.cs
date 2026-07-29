namespace McpClient.Mcpb;

/// <summary>
/// MCPB 解压中间件 — 安全解压 MCPB 包（含路径遍历检测、文件大小限制）
/// 缓存命中时跳过
/// </summary>
[Register(typeof(IMcpbMiddleware))]
public sealed partial class McpbExtractionMiddleware : IMcpbMiddleware
{
    [Inject] private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<McpbExtractionMiddleware>? _logger;

    private const int MaxFileSizeBytes = 512 * 1024 * 1024;
    private const int MaxTotalSizeBytes = 1024 * 1024 * 1024;


    public async Task InvokeAsync(McpbLoadContext context, MiddlewareDelegate<McpbLoadContext> next, CancellationToken ct)
    {
        if (context.IsCacheHit)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var mcpbPath = context.LocalFilePath;
        var extractPath = context.ExtractPath;

        _logger?.LogInformation("解压 MCPB: {Path} -> {ExtractPath}", mcpbPath, extractPath);

        if (_fs.DirectoryExists(extractPath))
        {
            try { _fs.DeleteDirectory(extractPath, true); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"McpbExtraction: Failed to delete directory: {ex.Message}"); }
        }

        _fs.CreateDirectory(extractPath);

        using var archive = System.IO.Compression.ZipFile.OpenRead(mcpbPath);
        long totalExtractedSize = 0;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            var destinationPath = Path.GetFullPath(Path.Combine(extractPath, entry.FullName));
            if (!destinationPath.StartsWith(extractPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"路径遍历检测: {entry.FullName}");
            }

            if (entry.Length > MaxFileSizeBytes)
            {
                throw new InvalidOperationException($"文件过大: {entry.FullName} ({entry.Length} bytes)");
            }

            var entryDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(entryDir) && !_fs.DirectoryExists(entryDir))
            {
                _fs.CreateDirectory(entryDir);
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var fileStream = _fs.CreateStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await entryStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);

            totalExtractedSize += _fs.GetFileLength(destinationPath);

            if (totalExtractedSize > MaxTotalSizeBytes)
            {
                throw new InvalidOperationException("解压总大小超过限制");
            }
        }

        _logger?.LogInformation("MCPB 解压完成: {Count} 个文件", archive.Entries.Count);

        await next(context, ct).ConfigureAwait(false);
    }
}
