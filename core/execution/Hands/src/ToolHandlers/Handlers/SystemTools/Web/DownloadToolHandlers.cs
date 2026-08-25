namespace Tools.Handlers;

/// <summary>
/// 下载工具处理器 — 多线程并发下载+断点续传,基于 RangeDownloader 基建
/// <para>通过 IDownloader 注入(Infrastructure 层 DI Singleton)</para>
/// <para>download_file 工具:启动下载→等待完成→返回结果</para>
/// </summary>
[McpToolDispatch(ToolCategory.Web)]
public class DownloadToolHandlers
{
    private readonly IDownloader _downloader;
    private readonly ITelemetryService? _telemetryService;

    public DownloadToolHandlers(IDownloader downloader, ITelemetryService? telemetryService = null)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _telemetryService = telemetryService;
    }

    [McpTool(WebToolNameConstants.DownloadFile, "下载文件到指定路径(支持多线程并发+断点续传)", "web", ConcurrencySafe = true)]
    public async Task<ToolResult> DownloadFileAsync(
        [McpToolParameter("下载 URL")] string url,
        [McpToolParameter("目标文件保存路径")] string file_path,
        [McpToolParameter("最大并发线程数(1-32,默认 4)", Required = false)] int? max_threads = null,
        [McpToolParameter("是否启用断点续传(默认 true)", Required = false)] bool? resume = null,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(url, "url"),
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateStringLength(url, 2048, "URL"),
            ValidationHelper.ValidateRange(max_threads, 1, 32, "max_threads"));
        if (validationError != null)
        {
            var diag = WebToolHandlers.BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var options = new DownloadOptions
        {
            MaxThreads = max_threads ?? 4,
            Resume = resume ?? true,
        };

        var session = _downloader.StartDownload(url, file_path, options, null, cancellationToken);
        try
        {
            var result = await session.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                RecordDownloadMetrics("failed", result.DownloadedBytes);
                var errorMsg = result.ErrorMessage ?? "下载失败";
                return ToolResultBuilder.Error()
                    .WithText(errorMsg)
                    .WithDiagnostic(ToolDiagnostic.Create("DownloadFailed", errorMsg,
                        [new DiagnosticDetail("url", url), new DiagnosticDetail("file_path", file_path)],
                        ["检查 URL 是否正确", "检查网络是否可用", "检查目标路径是否有写入权限"]))
                    .WithEntityMetadata(EntityMetadataEntry.Long("downloaded_bytes", result.DownloadedBytes))
                    .Build();
            }

            RecordDownloadMetrics("ok", result.TotalBytes);
            var sizeStr = ContentReplacementConstants.FormatFileSize(result.TotalBytes);
            return ToolResultBuilder.Success()
                .WithText($"下载完成: {file_path} ({sizeStr}, 耗时 {result.Elapsed.TotalSeconds:F1}s)")
                .WithEntityMetadata(EntityMetadataEntry.Long("total_bytes", result.TotalBytes))
                .WithEntityMetadata(EntityMetadataEntry.Long("downloaded_bytes", result.DownloadedBytes))
                .Build();
        }
        catch (OperationCanceledException)
        {
            return ToolResultBuilder.Error().WithText("下载已取消").Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error()
                .WithText($"下载异常: {ex.Message}")
                .WithDiagnostic(ToolDiagnostic.Create("DownloadException", ex.Message,
                    [new DiagnosticDetail("url", url), new DiagnosticDetail("file_path", file_path)],
                    ["检查 URL 是否正确", "检查网络是否可用", "检查目标路径是否有写入权限"]))
                .Build();
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void RecordDownloadMetrics(string result, long bytes)
    {
        ToolTelemetryHelper.RecordToolCount(_telemetryService, "download.count", "download", result);
        if (bytes > 0)
            ToolTelemetryHelper.RecordToolHistogram(_telemetryService, "download.size", (int)Math.Min(bytes, int.MaxValue),
                new Dictionary<string, string> { ["result"] = result }, "bytes", "Download size");
    }
}
