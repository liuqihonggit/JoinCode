namespace Tools.Handlers;

public partial class FileToolHandlers
{
    [McpTool(FileToolNameConstants.FileWrite, "Write a file to the local filesystem", "file")]
    public async Task<ToolResult> FileWriteAsync(
        [McpToolParameter("The absolute path to the file to write (must be absolute, not relative)")] string file_path,
        [McpToolParameter("The content to write to the file")] string content,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateStringLength(file_path, 4096, "file_path"));
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        if (IsUncPath(file_path))
        {
            var uncDiag = BuildUncPathWriteRejectedDiagnostic();
            return ToolResultBuilder.Error().WithText(uncDiag.FormattedMessage).WithDiagnostic(uncDiag).Build();
        }

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        // 对齐 TS: FileWriteTool.ts L156-160 — 拒绝写入团队记忆文件中的密钥
        if (_teamMemSecretGuard is not null)
        {
            var secretError = _teamMemSecretGuard.CheckTeamMemSecrets(file_path, content);
            if (secretError is not null)
            {
                var secretDiag = BuildTeamMemSecretRejectedDiagnostic(secretError);
                return ToolResultBuilder.Error().WithText(secretDiag.FormattedMessage).WithDiagnostic(secretDiag).Build();
            }
        }

        // Write-before-read validation: existing files must be read first
        // CLI 无状态模式（mcp_call 单次调用）下 FileStateCache 永远为空，跳过校验
        if (_fileStateCache is not null && _fs.FileExists(file_path) && !TestEnvironmentDetector.ForceNonInteractive)
        {
            if (!_fileStateCache.HasBeenRead(file_path))
            {
                RecordFileMetrics(FileOperationType.Write, FileOperationResult.Rejected);
                var notReadDiag = BuildFileNotReadBeforeWriteDiagnostic();
                return ToolResultBuilder.Error().WithText(notReadDiag.FormattedMessage).WithDiagnostic(notReadDiag).Build();
            }

            // Stale-write guard: check if file was modified after we read it
            var readTimestamp = _fileStateCache.GetReadTimestampMs(file_path);
            if (readTimestamp.HasValue)
            {
                var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(file_path)).ToUnixTimeMilliseconds();
                if (lastWriteMs > readTimestamp.Value + 1000) // 1s tolerance
                {
                    // Timestamp indicates modification, but on Windows timestamps can change
                    // without content changes (cloud sync, antivirus, etc.). Compare content
                    // as a fallback to avoid false positives (mirrors TS behavior).
                    var readContent = _fileStateCache.GetReadContent(file_path);
                    var isFullRead = readContent is not null;
                    if (isFullRead)
                    {
                        // 对齐 TS: 用检测到的编码读取文件，避免 UTF-16LE 文件内容比对错误
                        var detectedEncoding = await FileEncodingDetector.DetectFromFileAsync(file_path, _fs, cancellationToken).ConfigureAwait(false);
                        var currentContent = await _fs.ReadAllTextAsync(file_path, detectedEncoding, cancellationToken).ConfigureAwait(false);
                        if (currentContent == readContent)
                        {
                            // Content unchanged, safe to proceed
                        }
                        else
                        {
                        RecordFileMetrics(FileOperationType.Write, FileOperationResult.Stale);
                        var staleWriteDiag2 = BuildFileModifiedSinceReadDiagnostic("writing", file_path, lastWriteMs, readTimestamp.Value);
                        return ToolResultBuilder.Error().WithText(staleWriteDiag2.FormattedMessage).WithDiagnostic(staleWriteDiag2).Build();
                        }
                    }
                    else
                    {
                        RecordFileMetrics(FileOperationType.Write, FileOperationResult.Stale);
                        var staleWriteDiag = BuildFileModifiedSinceReadDiagnostic("writing", file_path, lastWriteMs, readTimestamp.Value);
                        return ToolResultBuilder.Error().WithText(staleWriteDiag.FormattedMessage).WithDiagnostic(staleWriteDiag).Build();
                    }
                }
            }
        }

        // Backup file before writing (if file exists)
        if (_fileHistoryService is not null && _fs.FileExists(file_path))
        {
            await _fileHistoryService.BackupBeforeWriteAsync(file_path, cancellationToken).ConfigureAwait(false);
        }

        FileWriteResult result;
        try
        {
            result = await _fileOperationService.WriteFileAsync(
                file_path,
                content,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordFileMetrics(FileOperationType.Write, FileOperationResult.Failed);
            _logger?.LogError(ex, "FileWrite 调用抛出异常: {FilePath}", file_path);
            var exDiagnostic = ToolDiagnostic.Create("WriteFailed",
                $"写入文件失败: {ex.Message}",
                [new DiagnosticDetail("filePath", file_path), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查文件权限、是否被其他进程锁定、磁盘空间是否充足。"]);
            return ToolResultBuilder.Error().WithText(exDiagnostic.FormattedMessage).WithDiagnostic(exDiagnostic).Build();
        }

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.Write, FileOperationResult.Failed);
            var builder = ToolResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to write file");
            if (result.Diagnostic is not null)
                builder = builder.WithDiagnostic(result.Diagnostic);
            return builder.Build();
        }

        var response = result.Operation == FileOperationTypeConstants.Create
            ? $"File created successfully at: {result.FilePath}"
            : $"The file {result.FilePath} has been updated successfully.";

        // 附加 structuredPatch 到 ToolResult — 对齐 TS FileWriteTool 返回 structuredPatch
        var toolResult = ToolResultBuilder.Success().WithText(response).Build();
        if (result.StructuredPatch.Any())
        {
            toolResult.StructuredPatch = result.StructuredPatch.ToArray();
        }

        // 对齐 TS: clearDeliveredDiagnosticsForFile — 写入后清除已投递诊断，让新诊断能重新展示
        _lspDiagnosticProvider?.ClearDeliveredForFile($"file://{result.FilePath}");

        // 对齐 TS: 写入成功后通知 LSP 服务器（fire-and-forget）
        // 1. changeFile（含 didOpen 自动回退）→ 2. saveFile
        NotifyLspFileChange(result.FilePath, content);

        RecordFileMetrics(FileOperationType.Write, FileOperationResult.Ok);
        NotifyFileWrite(result.FilePath, "write");
        return toolResult;
    }
}
