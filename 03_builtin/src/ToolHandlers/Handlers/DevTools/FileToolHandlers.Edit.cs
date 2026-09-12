namespace Tools.Handlers;

public partial class FileToolHandlers
{
    [McpTool(FileToolNameConstants.FileEdit, "Edit file contents by search-and-replace", "file")]
    public async Task<ToolResult> FileEditAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
        [McpToolParameter("String to replace (must match exactly)")] string old_string,
        [McpToolParameter("Replacement string")] string new_string,
        [McpToolParameter("Replace all occurrences, default false", Required = false, DefaultValue = "false")] bool replace_all = false,
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
            var uncDiag = BuildUncPathEditRejectedDiagnostic();
            return ToolResultBuilder.Error().WithText(uncDiag.FormattedMessage).WithDiagnostic(uncDiag).Build();
        }

        if (file_path.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase))
        {
            var notebookDiag = BuildNotebookEditRejectedDiagnostic();
            return ToolResultBuilder.Error().WithText(notebookDiag.FormattedMessage).WithDiagnostic(notebookDiag).Build();
        }

        if (old_string == new_string)
        {
            var identicalDiag = BuildIdenticalStringsDiagnostic();
            return ToolResultBuilder.Error().WithText(identicalDiag.FormattedMessage).WithDiagnostic(identicalDiag).Build();
        }

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        // 对齐 TS: FileEditTool.ts L143-147 — 拒绝编辑团队记忆文件时引入密钥
        if (_teamMemSecretGuard is not null)
        {
            var secretError = _teamMemSecretGuard.CheckTeamMemSecrets(file_path, new_string);
            if (secretError is not null)
            {
                var secretDiag = BuildTeamMemSecretRejectedDiagnostic(secretError);
                return ToolResultBuilder.Error().WithText(secretDiag.FormattedMessage).WithDiagnostic(secretDiag).Build();
            }
        }

        // 对齐 TS: FileEditTool.ts L345-359 — settings 文件编辑校验
        // 只阻止"从合法变非法"的降级编辑，不阻止修复
        if (SettingsEditValidator.IsJccSettingsPath(file_path) && _fs.FileExists(file_path))
        {
            var detectedEncoding = await FileEncodingDetector.DetectFromFileAsync(file_path, _fs, cancellationToken).ConfigureAwait(false);
            var originalContent = await _fs.ReadAllTextAsync(file_path, detectedEncoding, cancellationToken).ConfigureAwait(false);
            // 对齐 TS: 预模拟编辑 — 使用与 FileEditTool 相同的替换逻辑
            var updatedContent = replace_all
                ? originalContent.Replace(old_string, new_string)
                : ReplaceFirst(originalContent, old_string, new_string);
            var settingsError = SettingsEditValidator.ValidateEdit(file_path, originalContent, updatedContent);
            if (settingsError is not null)
            {
                RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Rejected);
                var settingsDiag = BuildSettingsEditRejectedDiagnostic(settingsError);
                return ToolResultBuilder.Error().WithText(settingsDiag.FormattedMessage).WithDiagnostic(settingsDiag).Build();
            }
        }

        // 关键词维护 Agent 编辑校验 — 限制只有 keywordMaintenance Agent 能编辑 keyword-sections.json
        if (IsKeywordSectionsPath(file_path) && _fs.FileExists(file_path))
        {
            var currentAgentType = _subAgentContextAccessor?.Current?.Variant?.ToValue() ?? _subAgentContextAccessor?.Current?.Role.ToValue();
            if (currentAgentType is not null && !currentAgentType.Equals("keywordMaintenance", StringComparison.OrdinalIgnoreCase))
            {
                RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Rejected);
                var keywordDiag = BuildKeywordSectionsEditRejectedDiagnostic();
                return ToolResultBuilder.Error().WithText(keywordDiag.FormattedMessage).WithDiagnostic(keywordDiag).Build();
            }
        }

        // doctor Agent 编辑校验 — 只能编辑 .jcc/diag/、.jcc/reflexion/ 和 worktree 内文件
        var doctorAgentType = _subAgentContextAccessor?.Current?.Variant?.ToValue() ?? _subAgentContextAccessor?.Current?.Role.ToValue();
        if (doctorAgentType is not null && doctorAgentType.Equals("doctor", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsDoctorAllowedEditPath(file_path))
            {
                RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Rejected);
                var doctorDiag = BuildDoctorAgentEditRejectedDiagnostic();
                return ToolResultBuilder.Error().WithText(doctorDiag.FormattedMessage).WithDiagnostic(doctorDiag).Build();
            }
        }

        // Write-before-read validation for edits too
        // CLI 无状态模式（mcp_call 单次调用）下 FileStateCache 永远为空，跳过校验
        if (_fileStateCache is not null && _fs.FileExists(file_path) && !TestEnvironmentDetector.ForceNonInteractive)
        {
            if (!_fileStateCache.HasBeenRead(file_path))
            {
                RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Rejected);
                var notReadDiag = BuildFileNotReadBeforeEditDiagnostic();
                return ToolResultBuilder.Error().WithText(notReadDiag.FormattedMessage).WithDiagnostic(notReadDiag).Build();
            }

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
                        RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Stale);
                        var staleEditDiag2 = BuildFileModifiedSinceReadDiagnostic("editing", file_path, lastWriteMs, readTimestamp.Value);
                        return ToolResultBuilder.Error().WithText(staleEditDiag2.FormattedMessage).WithDiagnostic(staleEditDiag2).Build();
                        }
                    }
                    else
                    {
                        RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Stale);
                        var staleEditDiag = BuildFileModifiedSinceReadDiagnostic("editing", file_path, lastWriteMs, readTimestamp.Value);
                        return ToolResultBuilder.Error().WithText(staleEditDiag.FormattedMessage).WithDiagnostic(staleEditDiag).Build();
                    }
                }
            }
        }

        // Backup file before editing
        if (_fileHistoryService is not null && _fs.FileExists(file_path))
        {
            await _fileHistoryService.BackupBeforeWriteAsync(file_path, cancellationToken).ConfigureAwait(false);
        }

        FileEditResult result;
        try
        {
            result = await _fileOperationService.EditFileAsync(
                file_path,
                old_string,
                new_string,
                replace_all,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Failed);
            _logger?.LogError(ex, "FileEdit 调用抛出异常: {FilePath}", file_path);
            var exDiagnostic = ToolDiagnostic.Create("EditFailed",
                $"编辑文件失败: {ex.Message}",
                [new DiagnosticDetail("filePath", file_path), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查文件权限、是否被其他进程锁定。"]);
            return ToolResultBuilder.Error().WithText(exDiagnostic.FormattedMessage).WithDiagnostic(exDiagnostic).Build();
        }

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Failed);
            var builder = ToolResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to edit file");
            if (result.Diagnostic is not null)
                builder = builder.WithDiagnostic(result.Diagnostic);
            return builder.Build();
        }

        var fileSize = ContentReplacementConstants.FormatFileSize(result.UpdatedContent.Length);
        var lineCount = result.UpdatedContent.AsSpan().Count('\n') + 1;
        var response = replace_all
            ? $"The file {result.FilePath} has been updated. All {result.ReplaceCount} occurrences were successfully replaced. ({fileSize}, {lineCount} lines)"
            : $"The file {result.FilePath} has been updated successfully. ({fileSize}, {lineCount} lines)";

        // 附加 structuredPatch 到 ToolResult — 对齐 TS FileEditTool 返回 structuredPatch
        var toolResult = ToolResultBuilder.Success().WithText(response).Build();
        if (result.StructuredPatch.Any())
        {
            toolResult.StructuredPatch = result.StructuredPatch.ToArray();
        }

        // 对齐 TS: clearDeliveredDiagnosticsForFile — 编辑后清除已投递诊断，让新诊断能重新展示
        _lspDiagnosticProvider?.ClearDeliveredForFile($"file://{result.FilePath}");

        // 对齐 TS: 编辑成功后通知 LSP 服务器（fire-and-forget）
        // 1. changeFile（含 didOpen 自动回退）→ 2. saveFile
        NotifyLspFileChange(result.FilePath, null);

        RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Ok);
        NotifyFileWrite(result.FilePath, "edit");
        return toolResult;
    }
    [McpTool(FileToolNameConstants.FileEditRegex, "Edit file using regex pattern to replace matched text", "file")]
    public async Task<ToolResult> FileEditRegexAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
        [McpToolParameter("Regex pattern")] string pattern,
        [McpToolParameter("Replacement string")] string replacement,
        [McpToolParameter("Replace all matches, default true", Required = false, DefaultValue = "true")] bool replace_all = true,
        CancellationToken cancellationToken = default)
    {
        if (_fileEditLogic == null)
        {
            var notInitDiag = BuildFileEditServiceNotInitializedDiagnostic();
            return ToolResultBuilder.Error().WithText(notInitDiag.FormattedMessage).WithDiagnostic(notInitDiag).Build();
        }

        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateRequired(pattern, "pattern"));
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        FileEditResult result;
        try
        {
            result = await _fileEditLogic.EditWithRegexAsync(file_path, pattern, replacement, replace_all, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordFileMetrics(FileOperationType.EditRegex, FileOperationResult.Failed);
            _logger?.LogError(ex, "FileEditRegex 调用抛出异常: {FilePath}", file_path);
            var exDiagnostic = ToolDiagnostic.Create("EditRegexFailed",
                $"正则编辑失败: {ex.Message}",
                [new DiagnosticDetail("filePath", file_path), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查正则表达式语法、文件权限。"]);
            return ToolResultBuilder.Error().WithText(exDiagnostic.FormattedMessage).WithDiagnostic(exDiagnostic).Build();
        }

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.EditRegex, FileOperationResult.Failed);
            var builder = ToolResultBuilder.Error().WithText(result.ErrorMessage ?? "Regex edit failed");
            if (result.Diagnostic is not null)
                builder = builder.WithDiagnostic(result.Diagnostic);
            return builder.Build();
        }

        var response = new StringBuilder(128);
        response.AppendLine($"File edited: {result.FilePath}");
        response.AppendLine($"Replaced {result.ReplaceCount} occurrence(s)");
        var regexFileSize = ContentReplacementConstants.FormatFileSize(result.UpdatedContent.Length);
        var regexLineCount = result.UpdatedContent.AsSpan().Count('\n') + 1;
        response.AppendLine($"({regexFileSize}, {regexLineCount} lines)");

        NotifyFileWrite(result.FilePath, "edit-regex");
        RecordFileMetrics(FileOperationType.EditRegex, FileOperationResult.Ok);
        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }
    [McpTool(FileToolNameConstants.FileInsertLines, "Insert new content after a specified line in the file", "file")]
    public async Task<ToolResult> FileInsertLinesAfterAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
        [McpToolParameter("Line number after which to insert (0 for file beginning)")] int after_line,
        [McpToolParameter("New content to insert")] string new_content,
        CancellationToken cancellationToken = default)
    {
        if (_fileEditLogic == null)
        {
            var notInitDiag = BuildFileEditServiceNotInitializedDiagnostic();
            return ToolResultBuilder.Error().WithText(notInitDiag.FormattedMessage).WithDiagnostic(notInitDiag).Build();
        }

        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateRequired(new_content, "new_content"),
            ValidationHelper.ValidateRange(after_line, 0, int.MaxValue, "after_line"));
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        FileLineEditResult result;
        try
        {
            result = await _fileEditLogic.InsertLinesAfterAsync(file_path, after_line, new_content, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordFileMetrics(FileOperationType.InsertLines, FileOperationResult.Failed);
            _logger?.LogError(ex, "FileInsertLines 调用抛出异常: {FilePath}", file_path);
            var exDiagnostic = ToolDiagnostic.Create("InsertLinesFailed",
                $"插入行失败: {ex.Message}",
                [new DiagnosticDetail("filePath", file_path), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查文件权限、是否被其他进程锁定。"]);
            return ToolResultBuilder.Error().WithText(exDiagnostic.FormattedMessage).WithDiagnostic(exDiagnostic).Build();
        }

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.InsertLines, FileOperationResult.Failed);
            var builder = ToolResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to insert lines");
            if (result.Diagnostic is not null)
                builder = builder.WithDiagnostic(result.Diagnostic);
            return builder.Build();
        }

        var response = new StringBuilder(128);
        response.AppendLine($"Content inserted: {result.FilePath}");
        response.AppendLine($"Inserted {result.ReplacedLinesCount} line(s) after line {after_line}");
        var insFileSize = ContentReplacementConstants.FormatFileSize(result.UpdatedFileContent.Length);
        var insLineCount = result.UpdatedFileContent.AsSpan().Count('\n') + 1;
        response.AppendLine($"({insFileSize}, {insLineCount} lines)");

        NotifyFileWrite(result.FilePath, "insert-lines");
        RecordFileMetrics(FileOperationType.InsertLines, FileOperationResult.Ok);
        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }
    [McpTool(FileToolNameConstants.FileDeleteLines, "Delete a range of lines from the file", "file")]
    public async Task<ToolResult> FileDeleteLinesAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
        [McpToolParameter("Start line number (1-based)")] int start_line,
        [McpToolParameter("End line number (1-based)")] int end_line,
        CancellationToken cancellationToken = default)
    {
        if (_fileEditLogic == null)
        {
            var notInitDiag = BuildFileEditServiceNotInitializedDiagnostic();
            return ToolResultBuilder.Error().WithText(notInitDiag.FormattedMessage).WithDiagnostic(notInitDiag).Build();
        }

        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateRange(start_line, 1, int.MaxValue, "start_line"),
            ValidationHelper.ValidateRange(end_line, 1, int.MaxValue, "end_line"));
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        FileLineEditResult result;
        try
        {
            result = await _fileEditLogic.DeleteLinesAsync(file_path, start_line, end_line, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordFileMetrics(FileOperationType.DeleteLines, FileOperationResult.Failed);
            _logger?.LogError(ex, "FileDeleteLines 调用抛出异常: {FilePath}", file_path);
            var exDiagnostic = ToolDiagnostic.Create("DeleteLinesFailed",
                $"删除行失败: {ex.Message}",
                [new DiagnosticDetail("filePath", file_path), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查文件权限、是否被其他进程锁定。"]);
            return ToolResultBuilder.Error().WithText(exDiagnostic.FormattedMessage).WithDiagnostic(exDiagnostic).Build();
        }

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.DeleteLines, FileOperationResult.Failed);
            var builder = ToolResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to delete lines");
            if (result.Diagnostic is not null)
                builder = builder.WithDiagnostic(result.Diagnostic);
            return builder.Build();
        }

        var response = new StringBuilder(128);
        response.AppendLine($"Lines deleted: {result.FilePath}");
        response.AppendLine($"Deleted {result.ReplacedLinesCount} line(s) ({result.StartLine}-{result.EndLine})");
        var delFileSize = ContentReplacementConstants.FormatFileSize(result.UpdatedFileContent.Length);
        var delLineCount = result.UpdatedFileContent.AsSpan().Count('\n') + 1;
        response.AppendLine($"({delFileSize}, {delLineCount} lines)");

        NotifyFileWrite(result.FilePath, "delete-lines");
        RecordFileMetrics(FileOperationType.DeleteLines, FileOperationResult.Ok);
        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }
    [McpTool(FileToolNameConstants.FileBatchEdit, "Batch edit multiple files with the same search-and-replace", "file")]
    public async Task<ToolResult> FileBatchEditAsync(
        [McpToolParameter("String to replace (must match exactly)")] string old_string,
        [McpToolParameter("Replacement string")] string new_string,
        [McpToolParameter("List of file paths")] string[]? file_paths = null,
        [McpToolParameter("Replace all matches, default true", Required = false, DefaultValue = "true")] bool replace_all = true,
        CancellationToken cancellationToken = default)
    {
        if (_fileEditLogic == null)
        {
            var notInitDiag = BuildFileEditServiceNotInitializedDiagnostic();
            return ToolResultBuilder.Error().WithText(notInitDiag.FormattedMessage).WithDiagnostic(notInitDiag).Build();
        }

        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(old_string, "old_string"));
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        if (file_paths == null || file_paths.Length == 0)
        {
            var noPathsDiag = BuildFilePathRequiredDiagnostic();
            return ToolResultBuilder.Error().WithText(noPathsDiag.FormattedMessage).WithDiagnostic(noPathsDiag).Build();
        }

        var resolvedPaths = await Task.WhenAll(
            file_paths.Select(path => ResolveSandboxPathAsync(path, cancellationToken))).ConfigureAwait(false);

        List<BatchEditResult> results;
        try
        {
            results = [.. await _fileEditLogic.BatchEditAsync(resolvedPaths, old_string, new_string, replace_all, cancellationToken).ConfigureAwait(false)];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordFileMetrics(FileOperationType.BatchEdit, FileOperationResult.Failed);
            _logger?.LogError(ex, "FileBatchEdit 调用抛出异常");
            var exDiagnostic = ToolDiagnostic.Create("BatchEditFailed",
                $"批量编辑失败: {ex.Message}",
                [new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查文件权限、是否被其他进程锁定。"]);
            return ToolResultBuilder.Error().WithText(exDiagnostic.FormattedMessage).WithDiagnostic(exDiagnostic).Build();
        }

        var response = new StringBuilder(512);
        response.AppendLine($"Batch edit completed: {results.Count} file(s)");
        response.AppendLine();

        var successCount = 0;
        var failureCount = 0;
        foreach (var item in results)
        {
            if (item.Result.Success)
            {
                successCount++;
                NotifyFileWrite(item.FilePath, "batch-edit");
                var batchSize = ContentReplacementConstants.FormatFileSize(item.Result.UpdatedContent.Length);
                response.AppendLine($"  {StatusSymbol.Tick.ToValue()} {item.FilePath} ({item.Result.ReplaceCount} replacement(s), {batchSize})");
            }
            else
            {
                failureCount++;
                response.AppendLine($"  {StatusSymbol.Cross.ToValue()} {item.FilePath}: {item.Result.ErrorMessage}");
            }
        }

        response.AppendLine();
        response.AppendLine($"Succeeded: {successCount}, Failed: {failureCount}");

        RecordFileMetrics(FileOperationType.BatchEdit, failureCount == 0 ? FileOperationResult.Ok : FileOperationResult.Partial);
        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }
}
