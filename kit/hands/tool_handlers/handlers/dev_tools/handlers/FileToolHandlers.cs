

namespace Tools.Handlers;

[McpToolDispatch(ToolCategory.File)]
public partial class FileToolHandlers : IDisposable
{
    private static readonly FrozenSet<string> BlockedDevicePaths = CreateBlockedDevicePathSet();
    private readonly CancellationTokenSource _disposeCts = new();

    /// <summary>
    /// LSP 通知完成信号量 — 测试中用于等待 fire-and-forget 操作完成，替代 Task.Delay
    /// 初始计数 0，NotifyLspFileChange 完成后释放 1 次
    /// </summary>
    private readonly SemaphoreSlim _lspNotificationCompleted = new(0, int.MaxValue);

    /// <summary>
    /// 图像扩展名集合（不作为二进制拒绝，而是读取为图像）
    /// 对齐 TS: IMAGE_EXTENSIONS = new Set(['png', 'jpg', 'jpeg', 'gif', 'webp'])
    /// </summary>
    private static readonly FrozenSet<string> ImageExtensions = FrozenSet.ToFrozenSet(
        ImageMediaTypeHelper.Extensions, StringComparer.OrdinalIgnoreCase);

    private const string MalwareReminder = """

        <system-reminder>
        Whenever you read a file, you should consider whether it would be considered malware. You CAN and SHOULD provide analysis of malware, what it is doing. But you MUST refuse to improve or augment the code. You can still analyze existing code, write reports, or behavior about the code.
        </system-reminder>
        """;

    private readonly IFileOperationService _fileOperationService;
    private readonly ISandboxManager? _sandboxManager;
    private readonly ITelemetryService? _telemetryService;
    private readonly FileEditLogic? _fileEditLogic;
    private readonly SnipLogic? _snipLogic;
    private readonly IFileStateCache? _fileStateCache;
    private readonly IFileHistoryService? _fileHistoryService;
    private readonly ILspFileSync? _lspFileSync;
    private readonly FileOperationConfig _fileOperationConfig;
    private readonly ITeamMemSecretGuard? _teamMemSecretGuard;
    private readonly IFileReadListenerRegistry? _fileReadListenerRegistry;
    private readonly IFileWriteListenerRegistry? _fileWriteListenerRegistry;
    private readonly ILspDiagnosticProvider? _lspDiagnosticProvider;
    private readonly IFileSystem _fs;
    private readonly ApplyPatchLogic? _applyPatchLogic;
    private readonly ISubAgentContextAccessor? _subAgentContextAccessor;
    private readonly ILogger<FileToolHandlers>? _logger;

    /// <summary>
    /// Default max read tokens (matches TS: DEFAULT_MAX_OUTPUT_TOKENS = 25000)
    /// </summary>
    private const int DefaultMaxReadTokens = 25000;

    public FileToolHandlers(
        IFileOperationService fileOperationService,
        IFileSystem fs,
        FileToolHandlersContext? context = null,
        ILogger<FileToolHandlers>? logger = null)
    {
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        _sandboxManager = context?.SandboxManager;
        _telemetryService = context?.TelemetryService;
        _fileEditLogic = context?.FileEditLogic;
        _snipLogic = context?.SnipLogic;
        _fileStateCache = context?.FileStateCache;
        _fileHistoryService = context?.FileHistoryService;
        _lspFileSync = context?.LspFileSync;
        _fileOperationConfig = context?.FileOperationConfig ?? new FileOperationConfig();
        _teamMemSecretGuard = context?.TeamMemSecretGuard;
        _fileReadListenerRegistry = context?.FileReadListenerRegistry;
        _fileWriteListenerRegistry = context?.FileWriteListenerRegistry;
        _lspDiagnosticProvider = context?.LspDiagnosticProvider;
        _applyPatchLogic = context?.ApplyPatchLogic;
        _subAgentContextAccessor = context?.SubAgentContextAccessor;
    }

    /// <summary>
    /// 通知文件写入监听器 — Worker 改文件时触发意图上报
    /// </summary>
    private void NotifyFileWrite(string filePath, string operation)
    {
        if (_fileWriteListenerRegistry is null) return;
        var agentId = _subAgentContextAccessor?.Current?.AgentId ?? "main";
        try
        {
            _fileWriteListenerRegistry.Notify(new FileWriteEventArgs { FilePath = filePath, Operation = operation, AgentId = agentId });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("通知文件写入监听器失败: {Message}", ex.Message);
        }
    }
    #region Diagnostics

    /// <summary>
    /// 通知 LSP 服务器文件变更（fire-and-forget）
    /// 对齐 TS FileWriteTool/FileEditTool: changeFile + saveFile
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="content">
    /// 文件完整内容。FileWrite 传入写入内容，FileEdit 传入 null（由 LspFileSync 从文件读取）
    /// </param>
    private void NotifyLspFileChange(string filePath, string? content)
    {
        if (_lspFileSync is null)
        {
            // LspFileSync 为 null 时立即释放信号量，避免测试等待超时
            _lspNotificationCompleted.Release();
            return;
        }

        var ct = _disposeCts.Token;
        // fire-and-forget: 不阻塞主流程，错误在 LspFileSync 内部处理
        _ = Task.Run(async () =>
        {
            try
            {
                // 对齐 TS: lspManager.changeFile(path, content)
                // LspFileSync.ChangeDocumentAsync 内部处理 didOpen 自动回退
                var changeContent = content;
                if (changeContent is null)
                {
                    // FileEdit 场景：从磁盘读取编辑后的内容（检测编码）
                    var encoding = await FileEncodingDetector.DetectFromFileAsync(filePath, _fs).ConfigureAwait(false);
                    changeContent = await _fs.ReadAllTextAsync(filePath, encoding).ConfigureAwait(false);
                }

                await _lspFileSync.ChangeDocumentAsync(
                    filePath,
                    [new TextDocumentContentChangeEvent { Text = changeContent }],
                    ct).WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);

                // 对齐 TS: lspManager.saveFile(path)
                await _lspFileSync.SaveDocumentAsync(filePath, ct)
                    .WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 取消时静默退出
            }
            catch (Exception ex)
            {
                // 对齐 TS: .catch() 处理错误，不阻塞主流程
                _telemetryService?.RecordCount("file.lsp_notify.error", new Dictionary<string, string>
                {
                    ["operation"] = "change_and_save",
                    ["error"] = ex.GetType().Name
                }, description: "LSP file change notification error");
            }
            finally
            {
                // 通知完成，释放信号量供测试等待
                _lspNotificationCompleted.Release();
            }
        }, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _disposeCts.CancelAndDisposeSafe(_logger);
        _lspNotificationCompleted.DisposeSafe(_logger);
    }

    private static FrozenSet<string> CreateBlockedDevicePathSet()
    {
        return FrozenSet.ToFrozenSet(
        [
            "/dev/zero", "/dev/random", "/dev/urandom", "/dev/full",
            "/dev/stdin", "/dev/tty", "/dev/console",
            "/dev/stdout", "/dev/stderr",
            "/dev/fd/0", "/dev/fd/1", "/dev/fd/2"
        ], StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBlockedDevicePath(string filePath)
    {
        if (BlockedDevicePaths.Contains(filePath))
            return true;

        if (filePath.StartsWith("/proc/", StringComparison.OrdinalIgnoreCase)
            && (filePath.EndsWith("/fd/0", StringComparison.OrdinalIgnoreCase)
                || filePath.EndsWith("/fd/1", StringComparison.OrdinalIgnoreCase)
                || filePath.EndsWith("/fd/2", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static bool IsUncPath(string filePath)
    {
        return filePath.StartsWith("\\\\", StringComparison.Ordinal)
               || filePath.StartsWith("//", StringComparison.Ordinal);
    }

    private static bool IsKeywordSectionsPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        return Path.GetFileName(filePath).Equals("keyword-sections.json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// doctor Agent 允许编辑的路径 — .jcc/diag/、.jcc/reflexion/、worktree 内文件
    /// </summary>
    private static bool IsDoctorAllowedEditPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var normalized = filePath.Replace('\\', '/');

        if (normalized.Contains("/.jcc/diag/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalized.Contains("/.jcc/reflexion/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalized.Contains("/worktree/", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// 替换字符串中第一个匹配项（用于预模拟编辑）。
    /// 对齐 TS: file.replace(actualOldString, new_string) — 非替换所有时只替换第一个匹配
    /// </summary>
    private static string ReplaceFirst(string text, string search, string replace)
    {
        var index = text.IndexOf(search, StringComparison.Ordinal);
        if (index < 0) return text;
        return string.Concat(text.AsSpan(0, index), replace, text.AsSpan(index + search.Length));
    }

    internal static string AddLineNumbers(string content, int startLine, bool compact)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var lines = content.Split(['\n'], StringSplitOptions.None);

        // 紧凑格式：行号 + 制表符 + 内容（cat -n 风格）
        // 对齐 TS: isCompactLinePrefixEnabled — LLM 训练数据匹配度高，每行省 ~5 空格 token
        if (compact)
        {
            var compactSb = new StringBuilder(content.Length + lines.Length * 4);
            for (var i = 0; i < lines.Length; i++)
            {
                compactSb.Append(startLine + i);
                compactSb.Append('\t');
                compactSb.AppendLine(lines[i]);
            }

            return compactSb.ToString();
        }

        // 宽格式：行号右对齐到 ≥6 位 + 管头 → + 内容
        var maxLineNum = startLine + lines.Length - 1;
        var maxDigits = maxLineNum.ToString().Length;
        var padWidth = Math.Max(maxDigits, 6);

        var sb = new StringBuilder(content.Length + lines.Length * (padWidth + 2));
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNum = startLine + i;
            sb.Append(lineNum.ToString().PadLeft(padWidth));
            sb.Append('\u2192');
            sb.AppendLine(lines[i]);
        }

        return sb.ToString();
    }

    private void RecordFileMetrics(FileOperationType operation, FileOperationResult result)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "file.operation.count", new Dictionary<string, string> { ["operation"] = operation.ToValue(), ["result"] = result.ToValue() });

    /// <summary>
    /// 记录文件读取详细遥测。
    /// 对齐 TS: tengu_session_file_read — 文本文件读取详情（行数/字节数/扩展名/会话文件类型）
    /// 对齐 TS: tengu_file_operation — 通用文件操作（路径哈希脱敏）
    /// </summary>
    private void RecordFileReadTelemetry(
        string filePath, string content, int totalLines, int readLines,
        int? offset, int? limit)
    {
        if (_telemetryService is null) return;

        // 对齐 TS: getFileExtensionForAnalytics — 脱敏扩展名（超过10字符替换为"other"）
        var ext = Path.GetExtension(filePath).TrimStart('.');
        var analyticsExt = string.IsNullOrEmpty(ext) ? null
            : ext.Length > 10 ? "other"
            : ext;

        // 对齐 TS: detectSessionFileType — 检测会话文件类型
        var isSessionMemory = MemoryFreshnessNote.IsMemoryFile(filePath);
        var isSessionTranscript = filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && filePath.Contains("projects", StringComparison.OrdinalIgnoreCase);

        // 对齐 TS: tengu_session_file_read
        var tags = new Dictionary<string, string>
        {
            ["total_lines"] = totalLines.ToString(CultureInfo.InvariantCulture),
            ["read_lines"] = readLines.ToString(CultureInfo.InvariantCulture),
            ["total_bytes"] = content.Length.ToString(CultureInfo.InvariantCulture),
            ["read_bytes"] = content.Length.ToString(CultureInfo.InvariantCulture),
            ["offset"] = offset?.ToString(CultureInfo.InvariantCulture) ?? "0",
            ["is_session_memory"] = isSessionMemory.ToString(),
            ["is_session_transcript"] = isSessionTranscript.ToString(),
        };
        if (limit.HasValue)
        {
            tags["limit"] = limit.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (analyticsExt is not null)
        {
            tags["ext"] = analyticsExt;
        }

        _telemetryService.RecordCount("file.read.detail", tags, description: "File read detail telemetry");

        // 对齐 TS: tengu_file_operation — 路径哈希脱敏
        var pathHash = SecurityPatterns.ComputeShortHash(filePath);
        _telemetryService.RecordCount("file.operation",
            new Dictionary<string, string> { ["operation"] = FileOperationTypeConstants.Read, ["path_hash"] = pathHash },
            description: "File operation with path hash");
    }

    /// <summary>
    /// 记录 PDF 读取遥测。
    /// 对齐 TS: tengu_pdf_page_extraction — PDF 页面提取事件
    /// </summary>
    private void RecordPdfReadTelemetry(string filePath, long fileSize, bool success)
    {
        if (_telemetryService is null) return;

        var tags = new Dictionary<string, string>
        {
            ["success"] = success.ToString(),
            ["file_size"] = fileSize.ToString(CultureInfo.InvariantCulture),
        };

        _telemetryService.RecordCount("file.read.pdf", tags, description: "PDF read telemetry");
    }

    /// <summary>
    /// 记录文件操作遥测（路径哈希脱敏）。
    /// 对齐 TS: tengu_file_operation — 通用文件操作事件
    /// </summary>
    private void RecordFileOperationTelemetry(string filePath, string operation)
    {
        if (_telemetryService is null) return;

        var pathHash = SecurityPatterns.ComputeShortHash(filePath);
        _telemetryService.RecordCount("file.operation.hash",
            new Dictionary<string, string> { ["operation"] = operation, ["path_hash"] = pathHash },
            description: "File operation with path hash");
    }

    private async Task<string> ResolveSandboxPathAsync(string path, CancellationToken cancellationToken)
    {
        if (_sandboxManager == null || !_sandboxManager.IsInSandbox)
        {
            return path;
        }

        var sandboxId = _sandboxManager.CurrentSandboxId;
        if (sandboxId is null)
        {
            return path;
        }

        var resolvedPath = _sandboxManager.ResolvePath(path, sandboxId);
        var isInSandbox = await _sandboxManager.ActiveProvider!.IsPathInSandboxAsync(resolvedPath, sandboxId, cancellationToken).ConfigureAwait(false);
        if (!isInSandbox)
        {
            throw new UnauthorizedAccessException($"Path '{path}' is outside the sandbox scope");
        }

        return resolvedPath;
    }

    /// <summary>
    /// 估算文本内容的Token数量。
    /// 对齐 TS: roughTokenCountEstimationForFileType — JSON/JSONL/JSONC 密集格式用 2 字节/token，
    /// 其他文件用 4 字节/token（密集格式的单字符 token 如 { } : , " 导致实际比率更低）。
    /// </summary>
    private static int EstimateTokenCount(string text, string? filePath = null)
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
    private static int BytesPerTokenForFileType(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return 4;
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        return ext is "json" or "jsonl" or "jsonc" ? 2 : 4;
    }

    /// <summary>
    /// 估算 base64 编码图像的 Token 数量。
    /// 对齐 TS: base64.length * 0.125（1 token ≈ 8 base64 字符 ≈ 6 原始字节）。
    /// TS 使用 compressImageBufferWithTokenLimit: maxBase64Chars = maxTokens / 0.125
    /// </summary>
    private static int EstimateImageTokenCount(int base64Length)
        => (int)(base64Length * 0.125);

    /// <summary>
    /// 读取图像文件并返回base64编码结果。
    /// 对齐 TS: readImageWithTokenBudget + maybeResizeAndDownsampleImageBuffer
    /// 流程：读取 → magic bytes检测 → 缩放/压缩 → base64编码 → token预算检查
    /// </summary>
    private async Task<ToolResult> ReadImageFileAsync(
        string filePath, string extension, CancellationToken cancellationToken)
    {
        filePath = await ResolveSandboxPathAsync(filePath, cancellationToken).ConfigureAwait(false);

        if (!_fs.FileExists(filePath))
        {
            // 对齐 TS: findSimilarFile + suggestPathUnderCwd — 文件未找到时建议相似文件
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

        // 读取原始图像字节
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

        // 用 magic bytes 检测实际格式（对齐 TS: detectImageFormatFromBuffer）
        var detectedType = ImageMediaTypeHelper.DetectFromMagicBytes(imageBytes);
        var effectiveExtension = detectedType is not null
            ? detectedType.Value.ToValue()
            : extension;

        // 缩放/压缩图像（对齐 TS: maybeResizeAndDownsampleImageBuffer）
        ImageResizeResult resizeResult;
        try
        {
            resizeResult = await ImageResizer.ResizeAsync(imageBytes, originalSize, effectiveExtension).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.ResizeFailed);
            var resizeDiagnostic = ToolDiagnostic.Create(
                "ImageResizeFailed",
                ex.Message,
                [new DiagnosticDetail("filePath", filePath), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查图像格式是否受支持，或使用更小的图像。"]);
            return ToolResultBuilder.Error().WithText(resizeDiagnostic.FormattedMessage).WithDiagnostic(resizeDiagnostic).Build();
        }

        // Base64 编码
        var base64Data = Convert.ToBase64String(resizeResult.Buffer);

        // 检查 base64 大小是否超过 API 限制（5MB）
        if (base64Data.Length > FileOperationConfig.ApiImageMaxBase64Size)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.ApiLimitExceeded);
            var base64Diag = BuildImageBase64TooLargeDiagnostic(base64Data.Length, FileOperationConfig.ApiImageMaxBase64Size);
            return ToolResultBuilder.Error().WithText(base64Diag.FormattedMessage).WithDiagnostic(base64Diag).Build();
        }

        // Token 预算检查（对齐 TS: base64.length * 0.125）
        var estimatedTokens = EstimateImageTokenCount(base64Data.Length);
        var maxTokens = _fileOperationConfig.MaxReadTokens > 0
            ? _fileOperationConfig.MaxReadTokens
            : DefaultMaxReadTokens;
        if (estimatedTokens > maxTokens)
        {
            // 对齐 TS: compressImageBufferWithTokenLimit — Token 驱动的激进压缩
            var compressedResult = await ImageResizer.CompressWithTokenBudgetAsync(
                resizeResult.Buffer, maxTokens, effectiveExtension).ConfigureAwait(false);

            if (compressedResult is not null)
            {
                // 激进压缩成功，重新编码
                base64Data = Convert.ToBase64String(compressedResult.Buffer);
                resizeResult = compressedResult;
                estimatedTokens = EstimateImageTokenCount(base64Data.Length);
            }
            else
            {
                // 所有压缩策略都无法满足预算
                RecordFileMetrics(FileOperationType.Read, FileOperationResult.TokenExceeded);
                var imgTokenDiag = BuildImageTokenExceededDiagnostic(estimatedTokens, resizeResult.Buffer.Length, maxTokens);
                return ToolResultBuilder.Error().WithText(imgTokenDiag.FormattedMessage).WithDiagnostic(imgTokenDiag).Build();
            }
        }

        // 记录读取状态
        var dimensionInfo = resizeResult.OriginalWidth is not null
            ? $" [{resizeResult.OriginalWidth}x{resizeResult.OriginalHeight} → {resizeResult.DisplayWidth}x{resizeResult.DisplayHeight}]"
            : string.Empty;
        _fileStateCache?.RecordRead(
            filePath,
            $"[image:{resizeResult.MediaType}:{originalSize}{dimensionInfo}]",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);

        // 返回图像结果：base64数据 + 文本摘要 + 元数据文本
        var summaryText = resizeResult.OriginalWidth is not null && resizeResult.OriginalWidth != resizeResult.DisplayWidth
            ? $"Read image: {filePath} (resized from {resizeResult.OriginalWidth}x{resizeResult.OriginalHeight} to {resizeResult.DisplayWidth}x{resizeResult.DisplayHeight}, {resizeResult.MediaType})"
            : $"Read image: {filePath} ({ContentReplacementConstants.FormatFileSize(resizeResult.Buffer.Length)}, {resizeResult.MediaType})";

        // 对齐 TS: createImageMetadataText — 缩放比例+坐标映射提示
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
    /// 对齐 TS: FileReadTool — PDF 读取决策树：
    /// 1. 用户指定 pages → 提取指定页面为 JPEG 图片（extractPDFPages）
    /// 2. 未指定 pages，页数 > 10 → 报错要求指定页码
    /// 3. 未指定 pages，页数 ≤ 10 → 检查 shouldExtractPages：
    ///    a. 文件 > 3MB → 提取全部页面为图片
    ///    b. 否则 → 直接发送 base64 PDF（readPDF）
    /// </summary>
    private async Task<ToolResult> ReadPdfFileAsync(
        string filePath, string? pages, CancellationToken cancellationToken)
    {
        // 对齐 TS: validateInput — 验证 pages 参数格式
        PdfPageRange? parsedRange = null;
        if (pages is not null)
        {
            parsedRange = PdfReader.ParsePageRange(pages);
            if (parsedRange is null)
            {
                var invalidPagesDiag = BuildPdfInvalidPagesDiagnostic(pages);
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
                var rangeExceedDiag = BuildPdfPageRangeExceedsMaxDiagnostic(pages, FileOperationConfig.PdfMaxPagesPerRead);
                return ToolResultBuilder.Error()
                    .WithText(rangeExceedDiag.FormattedMessage)
                    .WithDiagnostic(rangeExceedDiag)
                    .Build();
            }
        }

        filePath = await ResolveSandboxPathAsync(filePath, cancellationToken).ConfigureAwait(false);

        // 对齐 TS: 决策1 — 用户指定了 pages 参数 → 提取页面为图片
        if (parsedRange is not null)
        {
            return await ExtractPdfPagesAsync(filePath, parsedRange, cancellationToken).ConfigureAwait(false);
        }

        // 先获取 PDF 基本信息（大小 + 页数）
        var result = await PdfReader.ReadPdfAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.PdfFailed);
            var pdfDiagnostic = ToolDiagnostic.Create(
                "PdfReadFailed",
                result.ErrorMessage ?? "Failed to read PDF file",
                [new DiagnosticDetail("filePath", filePath)],
                ["检查文件是否为有效的 PDF，或使用 pages 参数读取特定页面。"]);
            return ToolResultBuilder.Error().WithText(pdfDiagnostic.FormattedMessage).WithDiagnostic(pdfDiagnostic).Build();
        }

        // 对齐 TS: 决策2 — 超过 PdfMaxInlinePageCount 页必须使用 pages 参数
        if (result.PageCount is not null &&
            result.PageCount > FileOperationConfig.PdfMaxInlinePageCount)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.Failed);
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

        // 对齐 TS: 决策3 — shouldExtractPages: 文件 > 3MB 时提取页面为图片
        // TS: shouldExtractPages = !isPDFSupported() || fileSize > PDF_EXTRACT_SIZE_THRESHOLD
        // C# 简化：始终支持 PDF base64 发送，仅文件过大时走提取路径
        if (result.OriginalSize > FileOperationConfig.PdfExtractSizeThreshold)
        {
            return await ExtractPdfPagesAsync(filePath, null, cancellationToken).ConfigureAwait(false);
        }

        // 对齐 TS: 决策3b — 文件较小，直接发送 base64 PDF
        // 记录读取状态
        _fileStateCache?.RecordRead(
            filePath,
            $"[pdf:{result.OriginalSize}bytes]",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        RecordPdfReadTelemetry(filePath, result.OriginalSize ?? 0, success: true);
        RecordFileOperationTelemetry(filePath, FileOperationTypeConstants.Read);
        RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);

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
    private async Task<ToolResult> ExtractPdfPagesAsync(
        string filePath, PdfPageRange? range, CancellationToken cancellationToken)
    {
        // 检查 PDFium 渲染功能是否可用
        if (!PdfPageRenderer.IsAvailable())
        {
            // 对齐 TS: pdftoppm 不可用时降级为 base64 发送
            var fallbackResult = await PdfReader.ReadPdfAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);
            if (!fallbackResult.Success)
            {
                RecordFileMetrics(FileOperationType.Read, FileOperationResult.PdfFailed);
                var fallbackDiag = BuildPdfFallbackReadFailedDiagnostic(fallbackResult.ErrorMessage ?? "Failed to read PDF file");
                return ToolResultBuilder.Error().WithText(fallbackDiag.FormattedMessage).WithDiagnostic(fallbackDiag).Build();
            }

            RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
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
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.PdfFailed);
            var extractDiag = BuildPdfExtractFailedDiagnostic(extractResult.ErrorMessage ?? "Failed to extract PDF pages");
            return ToolResultBuilder.Error().WithText(extractDiag.FormattedMessage).WithDiagnostic(extractDiag).Build();
        }

        // 记录读取状态
        _fileStateCache?.RecordRead(
            filePath,
            $"[pdf-extract:{extractResult.GetPages().Count()}pages]",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        RecordPdfReadTelemetry(filePath, extractResult.OriginalSize ?? 0, success: true);
        RecordFileOperationTelemetry(filePath, FileOperationTypeConstants.Read);

        // 对齐 TS: extractPDFPages → 读取输出目录中的 .jpg 文件 → maybeResizeAndDownsampleImageBuffer
        var builder = ToolResultBuilder.Success();
        var pageDescriptions = new List<string>();

        foreach (var page in extractResult.GetPages())
        {
            // 对齐 TS: maybeResizeAndDownsampleImageBuffer — 缩放/压缩每页图片
            ImageResizeResult resizeResult;
            try
            {
                resizeResult = await ImageResizer.ResizeAsync(
                    page.JpegBytes, page.JpegBytes.Length, "jpg").ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // 缩放失败，使用原始 JPEG
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

            // 检查 base64 大小是否超过 API 限制
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

            // 对齐 TS: createImageMetadataText — PDF 页面缩放时也附加坐标映射提示
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

        RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
        return builder.Build();
    }

    /// <summary>
    /// 读取 Notebook 文件并格式化输出
    /// 对齐 TS: FileReadTool → notebook.ts readNotebook + mapNotebookCellsToToolResult
    /// 支持 cell 输出中的图像作为 ImageBlock 发送
    /// </summary>
    private async Task<ToolResult> ReadNotebookFileAsync(
        string filePath, CancellationToken cancellationToken)
    {
        filePath = await ResolveSandboxPathAsync(filePath, cancellationToken).ConfigureAwait(false);

        // 对齐 TS: readFileState dedup — Notebook 也需要去重检查
        var existingState = _fileStateCache?.GetReadState(filePath);
        if (existingState is not null && !existingState.IsPartialView)
        {
            try
            {
                var currentMtimeMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();
                if (currentMtimeMs == existingState.TimestampMs)
                {
                    RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
                    return ToolResultBuilder.Success()
                        .WithText("File unchanged since last read. The content from the earlier Read tool_result in this conversation is still current — refer to that instead of re-reading.")
                        .Build();
                }
            }
            catch (Exception ex)
            {
                // stat 失败，降级为完整读取
                _logger?.LogWarning(ex, "Notebook 文件 stat 检查失败，降级为完整读取");
            }
        }

        var result = await NotebookReader.ReadNotebookAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.NotebookFailed);
            var nbDiagnostic = ToolDiagnostic.Create(
                "NotebookReadFailed",
                result.ErrorMessage ?? "Failed to read notebook file",
                [new DiagnosticDetail("filePath", filePath)],
                ["检查文件是否为有效的 Jupyter Notebook（.ipynb）格式。"]);
            return ToolResultBuilder.Error().WithText(nbDiagnostic.FormattedMessage).WithDiagnostic(nbDiagnostic).Build();
        }

        // 记录读取状态
        _fileStateCache?.RecordRead(
            filePath,
            $"[notebook:{result.Text?.Length ?? 0}chars]",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        // 对齐 TS: tengu_file_operation — Notebook 遥测
        RecordFileOperationTelemetry(filePath, FileOperationTypeConstants.Read);

        RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);

        // 对齐 TS: mapNotebookCellsToToolResult — 文本 + 图像块
        var builder = ToolResultBuilder.Success().WithText(result.GetText());

        // 对齐 TS: cellOutputToToolResult — 将 cell 输出中的图像作为 ImageBlock 发送
        if (result.Images is { Count: > 0 })
        {
            foreach (var image in result.Images)
            {
                builder.WithImage(image.Base64Data, image.MediaType);
            }
        }

        return builder.Build();
    }

    #endregion
}
