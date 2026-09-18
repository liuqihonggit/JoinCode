namespace Tools.Handlers;

/// <summary>
/// 特殊格式文件读取器 — 封装图像/PDF/Notebook 文件的读取与 token 估算
/// 从 FileToolHandlers 提取,统一管理非文本文件的读取逻辑
/// </summary>
internal sealed class FileSpecialFormatReader
{
    private static readonly FrozenSet<string> ImageExtensions = FrozenSet.ToFrozenSet(
        ImageMediaTypeHelper.Extensions, StringComparer.OrdinalIgnoreCase);

    private const int DefaultMaxReadTokens = 25000;

    private readonly IFileSystem _fs;
    private readonly FileOperationConfig _config;
    private readonly IFileStateCache? _stateCache;
    private readonly FileTelemetryRecorder _telemetry;
    private readonly FilePathResolver _pathResolver;
    private readonly ILogger? _logger;

    /// <summary>构造 FileSpecialFormatReader</summary>
    public FileSpecialFormatReader(IFileSystem fs, FileOperationConfig config, IFileStateCache? stateCache, FileTelemetryRecorder telemetry, FilePathResolver pathResolver, ILogger? logger = null)
    {
        _fs = fs;
        _config = config;
        _stateCache = stateCache;
        _telemetry = telemetry;
        _pathResolver = pathResolver;
        _logger = logger;
    }

    /// <summary>是否为图像扩展名</summary>
    public static bool IsImageExtension(string ext) => ImageExtensions.Contains(ext);

    /// <summary>
    /// 估算文本内容的Token数量。
    /// 对齐 TS: roughTokenCountEstimationForFileType — JSON/JSONL/JSONC 密集格式用 2 字节/token，
    /// 其他文件用 4 字节/token（密集格式的单字符 token 如 { } : , " 导致实际比率更低）。
    /// </summary>
    public static int EstimateTokenCount(string text, string? filePath = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var bytesPerToken = BytesPerTokenForFileType(filePath);
        return text.Length / bytesPerToken + (text.Length % bytesPerToken > 0 ? 1 : 0);
    }

    /// <summary>
    /// 根据文件扩展名返回字节/token比率。
    /// 对齐 TS: bytesPerTokenForFileType — JSON 密集格式用 2，其他用 4。
    /// </summary>
    public static int BytesPerTokenForFileType(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return 4;
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        return ext is "json" or "jsonl" or "jsonc" ? 2 : 4;
    }

    /// <summary>
    /// 估算 base64 编码图像的 Token 数量。
    /// 对齐 TS: base64.length * 0.125（1 token ≈ 8 base64 字符 ≈ 6 原始字节）。
    /// </summary>
    public static int EstimateImageTokenCount(int base64Length) => (int)(base64Length * 0.125);

    /// <summary>
    /// 读取图像文件并返回base64编码结果。
    /// 对齐 TS: readImageWithTokenBudget + maybeResizeAndDownsampleImageBuffer
    /// </summary>
    public async Task<ToolResult> ReadImageFileAsync(string filePath, string extension, CancellationToken cancellationToken)
    {
        filePath = await _pathResolver.ResolveSandboxPathAsync(filePath, cancellationToken).ConfigureAwait(false);

        if (!_fs.FileExists(filePath))
        {
            var diagnostic = FileSuggestionHelper.BuildFileNotFoundDiagnostic(filePath, _fs);
            return ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
        }

        var originalSize = _fs.GetFileLength(filePath);

        if (originalSize == 0)
        {
            var emptyDiagnostic = ToolDiagnostic.Create(
                "EmptyImageFile",
                $"Image file is empty: {filePath}",
                [new DiagnosticDetail("filePath", filePath), new DiagnosticDetail("size", "0")],
                ["检查文件是否正确写入或下载。"]);
            return ToolResultBuilder.Error().WithText(emptyDiagnostic.FormattedMessage).WithDiagnostic(emptyDiagnostic).Build();
        }

        byte[] imageBytes;
        try
        {
            imageBytes = await _fs.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var readDiagnostic = ToolDiagnostic.Create(
                "ImageReadFailed",
                $"Failed to read image file: {ex.Message}",
                [new DiagnosticDetail("filePath", filePath), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查文件权限、是否被其他进程锁定。"]);
            return ToolResultBuilder.Error().WithText(readDiagnostic.FormattedMessage).WithDiagnostic(readDiagnostic).Build();
        }

        var detectedType = ImageMediaTypeHelper.DetectFromMagicBytes(imageBytes);
        var effectiveExtension = detectedType is not null
            ? detectedType.Value.ToValue()
            : extension;

        ImageResizeResult resizeResult;
        try
        {
            resizeResult = await ImageResizer.ResizeAsync(imageBytes, originalSize, effectiveExtension).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _telemetry.RecordFileMetrics(FileOperationType.Read, FileOperationResult.ResizeFailed);
            var resizeDiagnostic = ToolDiagnostic.Create(
                "ImageResizeFailed",
                ex.Message,
                [new DiagnosticDetail("filePath", filePath), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查图像格式是否受支持，或使用更小的图像。"]);
            return ToolResultBuilder.Error().WithText(resizeDiagnostic.FormattedMessage).WithDiagnostic(resizeDiagnostic).Build();
        }

        var base64Data = Convert.ToBase64String(resizeResult.Buffer);

        if (base64Data.Length > FileOperationConfig.ApiImageMaxBase64Size)
        {
            _telemetry.RecordFileMetrics(FileOperationType.Read, FileOperationResult.ApiLimitExceeded);
            var base64Diag = FileToolHandlers.BuildImageBase64TooLargeDiagnostic(base64Data.Length, FileOperationConfig.ApiImageMaxBase64Size);
            return ToolResultBuilder.Error().WithText(base64Diag.FormattedMessage).WithDiagnostic(base64Diag).Build();
        }

        var estimatedTokens = EstimateImageTokenCount(base64Data.Length);
        var maxTokens = _config.MaxReadTokens > 0
            ? _config.MaxReadTokens
            : DefaultMaxReadTokens;
        if (estimatedTokens > maxTokens)
        {
            var compressedResult = await ImageResizer.CompressWithTokenBudgetAsync(
                resizeResult.Buffer, maxTokens, effectiveExtension).ConfigureAwait(false);

            if (compressedResult is not null)
            {
                base64Data = Convert.ToBase64String(compressedResult.Buffer);
                resizeResult = compressedResult;
                estimatedTokens = EstimateImageTokenCount(base64Data.Length);
            }
            else
            {
                _telemetry.RecordFileMetrics(FileOperationType.Read, FileOperationResult.TokenExceeded);
                var imgTokenDiag = FileToolHandlers.BuildImageTokenExceededDiagnostic(estimatedTokens, resizeResult.Buffer.Length, maxTokens);
                return ToolResultBuilder.Error().WithText(imgTokenDiag.FormattedMessage).WithDiagnostic(imgTokenDiag).Build();
            }
        }

        var dimensionInfo = resizeResult.OriginalWidth is not null
            ? $" [{resizeResult.OriginalWidth}x{resizeResult.OriginalHeight} → {resizeResult.DisplayWidth}x{resizeResult.DisplayHeight}]"
            : string.Empty;
        _stateCache?.RecordRead(
            filePath,
            $"[image:{resizeResult.MediaType}:{originalSize}{dimensionInfo}]",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        _telemetry.RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);

        var summaryText = resizeResult.OriginalWidth is not null && resizeResult.OriginalWidth != resizeResult.DisplayWidth
            ? $"Read image: {filePath} (resized from {resizeResult.OriginalWidth}x{resizeResult.OriginalHeight} to {resizeResult.DisplayWidth}x{resizeResult.DisplayHeight}, {resizeResult.MediaType})"
            : $"Read image: {filePath} ({ContentReplacementConstants.FormatFileSize(resizeResult.Buffer.Length)}, {resizeResult.MediaType})";

        var metadataText = ImageResizer.CreateImageMetadataText(resizeResult, filePath);
        if (metadataText is not null)
            summaryText += $"\n{metadataText}";

        return ToolResultBuilder.Success()
            .WithImage(base64Data, resizeResult.MediaType)
            .WithText(summaryText)
            .Build();
    }

    /// <summary>
    /// 读取 PDF 文件。
    /// 对齐 TS: FileReadTool — PDF 读取决策树
    /// </summary>
    public async Task<ToolResult> ReadPdfFileAsync(string filePath, string? pages, CancellationToken cancellationToken)
    {
        PdfPageRange? parsedRange = null;
        if (pages is not null)
        {
            parsedRange = PdfReader.ParsePageRange(pages);
            if (parsedRange is null)
            {
                var invalidPagesDiag = FileToolHandlers.BuildPdfInvalidPagesDiagnostic(pages);
                return ToolResultBuilder.Error()
                    .WithText(invalidPagesDiag.FormattedMessage)
                    .WithDiagnostic(invalidPagesDiag)
                    .Build();
            }

            var rangePageCount = parsedRange.LastPage == int.MaxValue
                ? FileOperationConfig.PdfMaxPagesPerRead
                : parsedRange.LastPage - parsedRange.FirstPage + 1;
            if (rangePageCount > FileOperationConfig.PdfMaxPagesPerRead)
            {
                var rangeExceedDiag = FileToolHandlers.BuildPdfPageRangeExceedsMaxDiagnostic(pages, FileOperationConfig.PdfMaxPagesPerRead);
                return ToolResultBuilder.Error()
                    .WithText(rangeExceedDiag.FormattedMessage)
                    .WithDiagnostic(rangeExceedDiag)
                    .Build();
            }
        }

        filePath = await _pathResolver.ResolveSandboxPathAsync(filePath, cancellationToken).ConfigureAwait(false);

        if (parsedRange is not null)
        {
            return await ExtractPdfPagesAsync(filePath, parsedRange, cancellationToken).ConfigureAwait(false);
        }

        var result = await PdfReader.ReadPdfAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            _telemetry.RecordFileMetrics(FileOperationType.Read, FileOperationResult.PdfFailed);
            var pdfDiagnostic = ToolDiagnostic.Create(
                "PdfReadFailed",
                result.ErrorMessage ?? "Failed to read PDF file",
                [new DiagnosticDetail("filePath", filePath)],
                ["检查文件是否为有效的 PDF，或使用 pages 参数读取特定页面。"]);
            return ToolResultBuilder.Error().WithText(pdfDiagnostic.FormattedMessage).WithDiagnostic(pdfDiagnostic).Build();
        }

        if (result.PageCount is not null &&
            result.PageCount > FileOperationConfig.PdfMaxInlinePageCount)
        {
            _telemetry.RecordFileMetrics(FileOperationType.Read, FileOperationResult.Failed);
            var pageDiagnostic = ToolDiagnostic.Create(
                "PdfTooManyPages",
                $"This PDF has {result.PageCount} pages, which is too many to read at once.",
                [
                    new DiagnosticDetail("filePath", filePath),
                    new DiagnosticDetail("pageCount", result.PageCount.Value.ToString()),
                    new DiagnosticDetail("maxInlinePages", FileOperationConfig.PdfMaxInlinePageCount.ToString()),
                    new DiagnosticDetail("maxPagesPerRead", FileOperationConfig.PdfMaxPagesPerRead.ToString()),
                ],
                [$"使用 pages 参数读取特定页面范围（如 pages: \"1-5\"），每次最多 {FileOperationConfig.PdfMaxPagesPerRead} 页。"]);
            return ToolResultBuilder.Error().WithText(pageDiagnostic.FormattedMessage).WithDiagnostic(pageDiagnostic).Build();
        }

        if (result.OriginalSize > FileOperationConfig.PdfExtractSizeThreshold)
        {
            return await ExtractPdfPagesAsync(filePath, null, cancellationToken).ConfigureAwait(false);
        }

        _stateCache?.RecordRead(
            filePath,
            $"[pdf:{result.OriginalSize}bytes]",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        _telemetry.RecordPdfReadTelemetry(filePath, result.OriginalSize ?? 0, success: true);
        _telemetry.RecordFileOperationTelemetry(filePath, FileOperationTypeEnumConstants.Read);
        _telemetry.RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);

        var pageCountInfo = result.PageCount is not null ? $", {result.PageCount} pages" : string.Empty;
        var summaryText = $"Read PDF: {filePath} ({ContentReplacementConstants.FormatFileSize(result.GetOriginalSize())}{pageCountInfo})";

        return ToolResultBuilder.Success()
            .WithPdf(result.GetBase64(), result.GetOriginalSize())
            .WithText(summaryText)
            .Build();
    }

    /// <summary>
    /// 提取 PDF 页面为 JPEG 图片。
    /// 对齐 TS: extractPDFPages — 使用 PDFium 渲染页面为 JPEG，再缩放/压缩
    /// </summary>
    public async Task<ToolResult> ExtractPdfPagesAsync(string filePath, PdfPageRange? range, CancellationToken cancellationToken)
    {
        if (!PdfPageRenderer.IsAvailable())
        {
            var fallbackResult = await PdfReader.ReadPdfAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);
            if (!fallbackResult.Success)
            {
                _telemetry.RecordFileMetrics(FileOperationType.Read, FileOperationResult.PdfFailed);
                var fallbackDiag = FileToolHandlers.BuildPdfFallbackReadFailedDiagnostic(fallbackResult.ErrorMessage ?? "Failed to read PDF file");
                return ToolResultBuilder.Error().WithText(fallbackDiag.FormattedMessage).WithDiagnostic(fallbackDiag).Build();
            }

            _telemetry.RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
            var fallbackInfo = fallbackResult.PageCount is not null ? $", {fallbackResult.PageCount} pages" : string.Empty;
            return ToolResultBuilder.Success()
                .WithPdf(fallbackResult.GetBase64(), fallbackResult.GetOriginalSize())
                .WithText($"Read PDF (rendering unavailable, sent as document): {filePath} ({ContentReplacementConstants.FormatFileSize(fallbackResult.GetOriginalSize())}{fallbackInfo})")
                .Build();
        }

        var firstPage = range?.FirstPage;
        var lastPage = range?.LastPage == int.MaxValue ? (int?)null : range?.LastPage;

        var extractResult = await PdfPageRenderer.ExtractPagesAsync(
            filePath, _fs, firstPage, lastPage, cancellationToken).ConfigureAwait(false);

        if (!extractResult.Success)
        {
            _telemetry.RecordFileMetrics(FileOperationType.Read, FileOperationResult.PdfFailed);
            var extractDiag = FileToolHandlers.BuildPdfExtractFailedDiagnostic(extractResult.ErrorMessage ?? "Failed to extract PDF pages");
            return ToolResultBuilder.Error().WithText(extractDiag.FormattedMessage).WithDiagnostic(extractDiag).Build();
        }

        _stateCache?.RecordRead(
            filePath,
            $"[pdf-extract:{extractResult.GetPages().Count()}pages]",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        _telemetry.RecordPdfReadTelemetry(filePath, extractResult.OriginalSize ?? 0, success: true);
        _telemetry.RecordFileOperationTelemetry(filePath, FileOperationTypeEnumConstants.Read);

        var builder = ToolResultBuilder.Success();
        var pageDescriptions = new List<string>();

        foreach (var page in extractResult.GetPages())
        {
            ImageResizeResult resizeResult;
            try
            {
                resizeResult = await ImageResizer.ResizeAsync(
                    page.JpegBytes, page.JpegBytes.Length, "jpg").ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                resizeResult = new ImageResizeResult
                {
                    Buffer = page.JpegBytes,
                    MediaType = "image/jpeg",
                    OriginalWidth = page.Width,
                    OriginalHeight = page.Height,
                    DisplayWidth = page.Width,
                    DisplayHeight = page.Height,
                };
            }

            var base64Data = Convert.ToBase64String(resizeResult.Buffer);

            if (base64Data.Length > FileOperationConfig.ApiImageMaxBase64Size)
            {
                pageDescriptions.Add($"Page {page.PageNumber}: too large to include ({ContentReplacementConstants.FormatFileSize(resizeResult.Buffer.Length)})");
                continue;
            }

            builder.WithImage(base64Data, resizeResult.MediaType);

            var dimensionInfo = resizeResult.OriginalWidth != resizeResult.DisplayWidth
                ? $" (resized {resizeResult.OriginalWidth}x{resizeResult.OriginalHeight} → {resizeResult.DisplayWidth}x{resizeResult.DisplayHeight})"
                : $" ({page.Width}x{page.Height})";
            var pageDesc = $"Page {page.PageNumber}{dimensionInfo}";

            var pageMetadata = ImageResizer.CreateImageMetadataText(resizeResult);
            var fullPageDesc = pageMetadata is not null
                ? string.Concat(pageDesc, "\n", pageMetadata)
                : pageDesc;

            pageDescriptions.Add(fullPageDesc);
        }

        var rangeText = range is not null ? $" pages {range.FirstPage}-{(range.LastPage == int.MaxValue ? "end" : range.LastPage.ToString())}" : string.Empty;
        var totalInfo = extractResult.TotalPageCount is not null ? $", {extractResult.TotalPageCount} total pages" : string.Empty;
        var summaryText = $"Read PDF: {filePath}{rangeText} — extracted {extractResult.GetPages().Count()} page(s){totalInfo}\n{string.Join("\n", pageDescriptions)}";

        builder.WithText(summaryText);

        _telemetry.RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
        return builder.Build();
    }

    /// <summary>
    /// 读取 Notebook 文件并格式化输出
    /// 对齐 TS: FileReadTool → notebook.ts readNotebook + mapNotebookCellsToToolResult
    /// </summary>
    public async Task<ToolResult> ReadNotebookFileAsync(string filePath, CancellationToken cancellationToken)
    {
        filePath = await _pathResolver.ResolveSandboxPathAsync(filePath, cancellationToken).ConfigureAwait(false);

        var existingState = _stateCache?.GetReadState(filePath);
        if (existingState is not null && !existingState.IsPartialView)
        {
            try
            {
                var currentMtimeMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();
                if (currentMtimeMs == existingState.TimestampMs)
                {
                    _telemetry.RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
                    return ToolResultBuilder.Success()
                        .WithText("File unchanged since last read. The content from the earlier Read tool_result in this conversation is still current — refer to that instead of re-reading.")
                        .Build();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Notebook 文件 stat 检查失败，降级为完整读取");
            }
        }

        var result = await NotebookReader.ReadNotebookAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            _telemetry.RecordFileMetrics(FileOperationType.Read, FileOperationResult.NotebookFailed);
            var nbDiagnostic = ToolDiagnostic.Create(
                "NotebookReadFailed",
                result.ErrorMessage ?? "Failed to read notebook file",
                [new DiagnosticDetail("filePath", filePath)],
                ["检查文件是否为有效的 Jupyter Notebook（.ipynb）格式。"]);
            return ToolResultBuilder.Error().WithText(nbDiagnostic.FormattedMessage).WithDiagnostic(nbDiagnostic).Build();
        }

        _stateCache?.RecordRead(
            filePath,
            $"[notebook:{result.Text?.Length ?? 0}chars]",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        _telemetry.RecordFileOperationTelemetry(filePath, FileOperationTypeEnumConstants.Read);
        _telemetry.RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);

        var builder = ToolResultBuilder.Success().WithText(result.GetText());

        if (result.Images is { Count: > 0 })
        {
            foreach (var image in result.Images)
            {
                builder.WithImage(image.Base64Data, image.MediaType);
            }
        }

        return builder.Build();
    }
}
