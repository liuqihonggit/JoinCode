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
        // ── 参数校验 ──
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateStringLength(file_path, 4096, "file_path"));
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        // ── 统一写入防御链 — FileEdit 独有校验全部进链按需调用 ──
        var safety = await WriteDefense
            .Begin(file_path, new_string, FileOperationType.Edit, "editing", old_string, new_string, replace_all)
            .Then(RejectUncPath)              // UNC 路径拒绝（防凭据泄露）
            .Then(RejectNotebookEdit)         // .ipynb 拒绝（引导用 NotebookEdit）
            .Then(RejectIdenticalStrings)     // old==new 无变化拒绝
            .Then(ResolveSandboxAsync)        // 沙箱路径解析
            .Then(CheckTeamMemSecrets)        // 团队密钥检测
            .Then(ValidateSettingsEditAsync)  // settings 文件合法性校验
            .Then(ValidateKeywordSectionsEdit)// keyword-sections.json 权限校验
            .Then(ValidateDoctorAgentEdit)    // doctor Agent 路径范围校验
            .Then(RequireReadBeforeWrite)     // 写前读校验
            .Then(GuardStaleWriteAsync)       // 脏写保护
            .Then(BackupBeforeWriteAsync)     // 写前备份
            .ExecuteAsync(cancellationToken).ConfigureAwait(false);

        if (safety.Rejection is not null) return safety.Rejection;
        file_path = safety.Context.ResolvedPath;

        // ── 执行编辑 ──
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

        // ── 构建成功响应 ──
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

        // ── 统一写入后通知 — LSP 诊断清除 + LSP 文件变更 + 遥测 + 写入监听器 ──
        NotifyWriteComplete(result.FilePath, null, "edit", FileOperationType.Edit);
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

        // ── 参数校验 ──
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateRequired(pattern, "pattern"));
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        // ── 统一写入防御链 — replacement 可能含密钥，必须检测 ──
        var safety = await WriteDefense
            .Begin(file_path, replacement, FileOperationType.EditRegex, "editing")
            .Then(RejectUncPath)           // UNC 路径拒绝
            .Then(RejectNotebookEdit)      // .ipynb 拒绝
            .Then(ResolveSandboxAsync)     // 沙箱路径解析
            .Then(CheckTeamMemSecrets)     // 团队密钥检测（replacement 可能含密钥）
            .Then(RequireReadBeforeWrite)  // 写前读校验
            .Then(GuardStaleWriteAsync)    // 脏写保护
            .Then(BackupBeforeWriteAsync)  // 写前备份
            .ExecuteAsync(cancellationToken).ConfigureAwait(false);

        if (safety.Rejection is not null) return safety.Rejection;
        file_path = safety.Context.ResolvedPath;

        // ── 执行正则编辑 ──
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

        // ── 构建成功响应 ──
        var response = new StringBuilder(128);
        response.AppendLine($"File edited: {result.FilePath}");
        response.AppendLine($"Replaced {result.ReplaceCount} occurrence(s)");
        var regexFileSize = ContentReplacementConstants.FormatFileSize(result.UpdatedContent.Length);
        var regexLineCount = result.UpdatedContent.AsSpan().Count('\n') + 1;
        response.AppendLine($"({regexFileSize}, {regexLineCount} lines)");

        // ── 统一写入后通知 ──
        NotifyWriteComplete(result.FilePath, null, "edit-regex", FileOperationType.EditRegex);
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

        // ── 参数校验 ──
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateRequired(new_content, "new_content"),
            ValidationHelper.ValidateRange(after_line, 0, int.MaxValue, "after_line"));
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        // ── 统一写入防御链 — new_content 可能含密钥，必须检测 ──
        var safety = await WriteDefense
            .Begin(file_path, new_content, FileOperationType.InsertLines, "inserting")
            .Then(RejectUncPath)           // UNC 路径拒绝
            .Then(RejectNotebookEdit)      // .ipynb 拒绝
            .Then(ResolveSandboxAsync)     // 沙箱路径解析
            .Then(CheckTeamMemSecrets)     // 团队密钥检测（new_content 可能含密钥）
            .Then(RequireReadBeforeWrite)  // 写前读校验
            .Then(GuardStaleWriteAsync)    // 脏写保护
            .Then(BackupBeforeWriteAsync)  // 写前备份
            .ExecuteAsync(cancellationToken).ConfigureAwait(false);

        if (safety.Rejection is not null) return safety.Rejection;
        file_path = safety.Context.ResolvedPath;

        // ── 执行插入行 ──
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

        // ── 构建成功响应 ──
        var response = new StringBuilder(128);
        response.AppendLine($"Content inserted: {result.FilePath}");
        response.AppendLine($"Inserted {result.ReplacedLinesCount} line(s) after line {after_line}");
        var insFileSize = ContentReplacementConstants.FormatFileSize(result.UpdatedFileContent.Length);
        var insLineCount = result.UpdatedFileContent.AsSpan().Count('\n') + 1;
        response.AppendLine($"({insFileSize}, {insLineCount} lines)");

        // ── 统一写入后通知 ──
        NotifyWriteComplete(result.FilePath, null, "insert-lines", FileOperationType.InsertLines);
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

        // ── 参数校验 ──
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateRange(start_line, 1, int.MaxValue, "start_line"),
            ValidationHelper.ValidateRange(end_line, 1, int.MaxValue, "end_line"));
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        // ── 统一写入防御链 — 删除行不引入新内容，跳过密钥检测（ContentToCheck=null） ──
        var safety = await WriteDefense
            .Begin(file_path, null, FileOperationType.DeleteLines, "deleting lines")
            .Then(RejectUncPath)           // UNC 路径拒绝
            .Then(RejectNotebookEdit)      // .ipynb 拒绝
            .Then(ResolveSandboxAsync)     // 沙箱路径解析
            .Then(RequireReadBeforeWrite)  // 写前读校验
            .Then(GuardStaleWriteAsync)    // 脏写保护
            .Then(BackupBeforeWriteAsync)  // 写前备份
            .ExecuteAsync(cancellationToken).ConfigureAwait(false);

        if (safety.Rejection is not null) return safety.Rejection;
        file_path = safety.Context.ResolvedPath;

        // ── 执行删除行 ──
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

        // ── 构建成功响应 ──
        var response = new StringBuilder(128);
        response.AppendLine($"Lines deleted: {result.FilePath}");
        response.AppendLine($"Deleted {result.ReplacedLinesCount} line(s) ({result.StartLine}-{result.EndLine})");
        var delFileSize = ContentReplacementConstants.FormatFileSize(result.UpdatedFileContent.Length);
        var delLineCount = result.UpdatedFileContent.AsSpan().Count('\n') + 1;
        response.AppendLine($"({delFileSize}, {delLineCount} lines)");

        // ── 统一写入后通知 ──
        NotifyWriteComplete(result.FilePath, null, "delete-lines", FileOperationType.DeleteLines);
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

        // ── 参数校验 ──
        var validationError = ValidationHelper.ValidateRequired(old_string, "old_string");
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

        // ── 统一写入防御链 — 对每个文件并行跑防御链，任一拒绝则该文件跳过编辑 ──
        var defenses = await Task.WhenAll(
            file_paths.Select(async path =>
            {
                var safety = await WriteDefense
                    .Begin(path, new_string, FileOperationType.BatchEdit, "batch-editing", old_string, new_string, replace_all)
                    .Then(RejectUncPath)           // UNC 路径拒绝
                    .Then(RejectNotebookEdit)      // .ipynb 拒绝
                    .Then(ResolveSandboxAsync)     // 沙箱路径解析
                    .Then(CheckTeamMemSecrets)     // 团队密钥检测（new_string 可能含密钥）
                    .Then(RequireReadBeforeWrite)  // 写前读校验
                    .Then(GuardStaleWriteAsync)    // 脏写保护
                    .Then(BackupBeforeWriteAsync)  // 写前备份
                    .ExecuteAsync(cancellationToken).ConfigureAwait(false);
                return (OriginalPath: path, Safety: safety);
            })).ConfigureAwait(false);

        // 防御被拒绝的文件直接计入失败结果
        var rejectedFiles = defenses
            .Where(d => d.Safety.Rejection is not null)
            .Select(d => (d.OriginalPath, d.Safety.Rejection!))
            .ToList();

        // 防御通过的文件执行批量编辑
        var resolvedPaths = defenses
            .Where(d => d.Safety.Rejection is null)
            .Select(d => d.Safety.Context.ResolvedPath)
            .ToArray();

        List<BatchEditResult> results;
        if (resolvedPaths.Length > 0)
        {
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
        }
        else
        {
            results = [];
        }

        // ── 构建成功响应（含防御拒绝的文件） ──
        var response = new StringBuilder(512);
        response.AppendLine($"Batch edit completed: {results.Count + rejectedFiles.Count} file(s)");
        response.AppendLine();

        var successCount = 0;
        var failureCount = 0;

        // 防御被拒绝的文件
        foreach (var (rejectedPath, rejection) in rejectedFiles)
        {
            failureCount++;
            response.AppendLine($"  {StatusSymbol.Cross.ToValue()} {rejectedPath}: {rejection.Content}");
        }

        // 编辑执行结果
        foreach (var item in results)
        {
            if (item.Result.Success)
            {
                successCount++;
                // ── 统一写入后通知（每个成功文件） ──
                NotifyWriteComplete(item.FilePath, null, "batch-edit", FileOperationType.BatchEdit);
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
