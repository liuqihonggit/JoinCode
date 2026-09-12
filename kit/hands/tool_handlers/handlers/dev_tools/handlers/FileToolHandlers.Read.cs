namespace Tools.Handlers;

public partial class FileToolHandlers
{
    [McpTool(FileToolNameConstants.FileRead, "Read a file from the local filesystem", "file", ConcurrencySafe = true)]
    public async Task<ToolResult> FileReadAsync(
        [McpToolParameter("The absolute path to the file to read")] string file_path,
        [McpToolParameter("The line number to start reading from (1-based). Only use for large files.", Required = false)] int? offset = null,
        [McpToolParameter("The number of lines to read. Only use for large files.", Required = false)] int? limit = null,
        [McpToolParameter("Page range for PDF files (e.g., \"1-5\", \"3\", \"10-20\"). Only applicable to PDF files. Maximum 20 pages per request.", Required = false)] string? pages = null,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateStringLength(file_path, 4096, "file_path"),
            ValidationHelper.ValidateRange(offset, 1, int.MaxValue, "offset"),
            ValidationHelper.ValidateRange(limit, 1, int.MaxValue, "limit"));
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        if (IsUncPath(file_path))
        {
            var uncDiagnostic = ToolDiagnostic.Create(
                "UncPathRejected",
                "Cannot read UNC path files (starting with \\\\), this may lead to credential leakage.",
                [new DiagnosticDetail("filePath", file_path)],
                ["使用本地文件路径替代 UNC 路径。"]);
            return ToolResultBuilder.Error().WithText(uncDiagnostic.FormattedMessage).WithDiagnostic(uncDiagnostic).Build();
        }

        if (IsBlockedDevicePath(file_path))
        {
            var devDiagnostic = ToolDiagnostic.Create(
                "DevicePathRejected",
                $"Cannot read '{file_path}': this device file would block or produce infinite output.",
                [new DiagnosticDetail("filePath", file_path)],
                ["使用常规文件路径，避免读取设备文件（如 CON、PRN、NUL、COM1-9、LPT1-9）。"]);
            return ToolResultBuilder.Error().WithText(devDiagnostic.FormattedMessage).WithDiagnostic(devDiagnostic).Build();
        }

        var ext = Path.GetExtension(file_path).ToLowerInvariant();
        var extWithoutDot = ext.Length > 0 ? ext[1..] : string.Empty;

        // 图像文件特殊处理（不作为二进制拒绝，而是读取为图像）
        if (ImageExtensions.Contains(extWithoutDot))
        {
            return await ReadImageFileAsync(file_path, extWithoutDot, cancellationToken).ConfigureAwait(false);
        }

        // 对齐 TS: FileReadTool — PDF 文件特殊处理（不作为二进制拒绝，而是读取为 base64）
        if (PdfReader.IsPdfExtension(file_path))
        {
            return await ReadPdfFileAsync(file_path, pages, cancellationToken).ConfigureAwait(false);
        }

        // 对齐 TS: FileReadTool — Notebook 文件特殊处理（不作为二进制拒绝，而是格式化输出）
        if (NotebookReader.IsNotebookExtension(file_path))
        {
            return await ReadNotebookFileAsync(file_path, cancellationToken).ConfigureAwait(false);
        }

        if (BinaryFileDetector.IsBinaryExtension(ext))
        {
            var binExtDiagnostic = ToolDiagnostic.Create(
                "BinaryExtensionRejected",
                $"This tool cannot read binary files. The file appears to be a binary {ext} file.",
                [new DiagnosticDetail("filePath", file_path), new DiagnosticDetail("extension", ext)],
                [$"使用适当的工具分析二进制文件（如 {FileToolNameConstants.FileRead} 读取图片、{FileToolNameConstants.FileRead} 读取 PDF）。"]);
            return ToolResultBuilder.Error().WithText(binExtDiagnostic.FormattedMessage).WithDiagnostic(binExtDiagnostic).Build();
        }

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        // 对齐 TS: readFileState dedup — 检查文件是否已读取且未修改
        // 约 18% 的 Read 调用是同文件碰撞，去重可节省 cache_creation token
        var existingState = _fileStateCache?.GetReadState(file_path);
        if (existingState is not null && !existingState.IsPartialView && existingState.Offset.HasValue)
        {
            var rangeMatch = existingState.Offset == (offset.HasValue ? offset.Value - 1 : (int?)null)
                && existingState.Limit == limit;
            if (rangeMatch)
            {
                try
                {
                    var currentMtimeMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(file_path)).ToUnixTimeMilliseconds();
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
                    // stat 失败（文件可能被删除），降级为完整读取
                    _logger?.LogWarning(ex, "文件 stat 检查失败，降级为完整读取");
                }
            }
        }

        var fileOffset = offset.HasValue ? offset.Value - 1 : (int?)null;

        FileReadResult result;
        try
        {
            result = await _fileOperationService.ReadFileAsync(
                file_path,
                fileOffset,
                limit,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.Failed);
            _logger?.LogError(ex, "FileRead 调用抛出异常: {FilePath}", file_path);
            var exDiagnostic = ToolDiagnostic.Create(
                "ReadFailed",
                $"读取文件失败: {ex.Message}",
                [
                    new DiagnosticDetail("filePath", file_path),
                    new DiagnosticDetail("exceptionType", ex.GetType().Name),
                ],
                ["检查文件权限、是否被其他进程锁定。"]);
            return ToolResultBuilder.Error().WithText(exDiagnostic.FormattedMessage).WithDiagnostic(exDiagnostic).Build();
        }

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.Failed);
            var builder = ToolResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to read file");
            if (result.Diagnostic is not null)
                builder = builder.WithDiagnostic(result.Diagnostic);
            return builder.Build();
        }

        if (result.TotalLines == 0)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
            return ToolResultBuilder.Success().WithText("<system-reminder>Warning: the file exists but the contents are empty.</system-reminder>").Build();
        }

        if (result.NumLines == 0 && offset.HasValue && offset.Value > result.TotalLines)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
            return ToolResultBuilder.Success().WithText($"<system-reminder>Warning: the file exists but is shorter than the provided offset ({offset.Value}). The file has {result.TotalLines} lines.</system-reminder>").Build();
        }

        // Token limit check (matches TS: validateContentTokens)
        // Prevents reading files that would consume too much context
        var maxTokens = _fileOperationConfig.MaxReadTokens > 0
            ? _fileOperationConfig.MaxReadTokens
            : DefaultMaxReadTokens;
        var estimatedTokens = EstimateTokenCount(result.Content, file_path);
        if (estimatedTokens > maxTokens)
        {
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.TokenExceeded);
            var tokenDiagnostic = ToolDiagnostic.Create(
                "TokenLimitExceeded",
                $"File content ({estimatedTokens} tokens) exceeds maximum allowed tokens ({maxTokens}).",
                [
                    new DiagnosticDetail("filePath", file_path),
                    new DiagnosticDetail("estimatedTokens", estimatedTokens.ToString()),
                    new DiagnosticDetail("maxTokens", maxTokens.ToString()),
                ],
                ["使用 offset 和 limit 参数读取文件的部分内容。", $"使用 {SearchToolNameConstants.Grep} 工具搜索特定内容而非读取整个文件。"]);
            return ToolResultBuilder.Error().WithText(tokenDiagnostic.FormattedMessage).WithDiagnostic(tokenDiagnostic).Build();
        }

        var numberedContent = AddLineNumbers(result.Content, result.StartLine, _fileOperationConfig.CompactLinePrefix);

        var response = new StringBuilder(256);
        response.Append(numberedContent);

        // 对齐 TS: FileReadTool — 记忆文件新鲜度提示
        if (MemoryFreshnessNote.IsMemoryFile(file_path))
        {
            var mtimeMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(file_path)).ToUnixTimeMilliseconds();
            var freshnessNote = MemoryFreshnessNote.FreshnessNote(mtimeMs);
            if (!string.IsNullOrEmpty(freshnessNote))
            {
                response.Append(freshnessNote);
            }
        }

        response.Append(MalwareReminder);

        // Record read state for write-before-read validation
        // 对齐 TS: timestamp 使用文件 mtime 而非当前时间，用于去重判断
        long recordTimestampMs;
        try
        {
            recordTimestampMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(result.FilePath)).ToUnixTimeMilliseconds();
        }
        catch
        {
            recordTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        _fileStateCache?.RecordRead(
            result.FilePath,
            result.Content,
            recordTimestampMs,
            offset.HasValue ? offset.Value - 1 : null,
            limit);

        // 对齐 TS: FileReadTool — 通知文件读取监听器
        // 仅在文本文件读取成功后触发，PDF/Notebook/图像等特殊文件不触发
        _fileReadListenerRegistry?.Notify(new FileReadEventArgs
        {
            FilePath = result.FilePath,
            Content = result.Content,
        });

        // 对齐 TS: tengu_session_file_read + tengu_file_operation — 详细遥测
        RecordFileReadTelemetry(result.FilePath, result.Content, result.TotalLines, result.NumLines, offset, limit);

        RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }
}
