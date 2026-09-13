namespace Tools.Handlers;

public partial class FileToolHandlers
{
    [McpTool(FileToolNameConstants.FileSnipLines, "Read a range of lines from the file (snip read)", "file", ConcurrencySafe = true)]
    public async Task<ToolResult> FileSnipLinesAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
        [McpToolParameter("Start line number (0-based)", Required = false, DefaultValue = "0")] int start_line = 0,
        [McpToolParameter("Line count limit", Required = false, DefaultValue = "100")] int line_count = 100,
        CancellationToken cancellationToken = default)
    {
        if (_snipLogic == null)
        {
            var notInitDiag = BuildFileChunkingServiceNotInitializedDiagnostic();
            return ToolResultBuilder.Error().WithText(notInitDiag.FormattedMessage).WithDiagnostic(notInitDiag).Build();
        }

        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateRange(start_line, 0, int.MaxValue, "start_line"),
            ValidationHelper.ValidateRange(line_count, 1, int.MaxValue, "line_count"));
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        string content;
        try
        {
            content = await _snipLogic.SnipLinesAsync(file_path, start_line, line_count, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            RecordFileMetrics(FileOperationType.SnipLines, FileOperationResult.Failed);
            var diagnostic = FileSuggestionHelper.BuildFileNotFoundDiagnostic(file_path, _fs);
            return ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordFileMetrics(FileOperationType.SnipLines, FileOperationResult.Failed);
            _logger?.LogError(ex, "SnipLines 调用抛出异常: {FilePath}", file_path);
            var exDiagnostic = ToolDiagnostic.Create("SnipLinesFailed",
                $"截取行失败: {ex.Message}",
                [new DiagnosticDetail("filePath", file_path), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查文件权限、是否被其他进程锁定。"]);
            return ToolResultBuilder.Error().WithText(exDiagnostic.FormattedMessage).WithDiagnostic(exDiagnostic).Build();
        }

        var response = new StringBuilder(256);
        response.AppendLine($"File: {file_path}");
        response.AppendLine($"Line range: {start_line}-{start_line + line_count - 1}");
        response.AppendLine();
        response.AppendLine("```");
        response.Append(content.TrimEnd());
        response.AppendLine();
        response.AppendLine("```");

        RecordFileMetrics(FileOperationType.SnipLines, FileOperationResult.Ok);
        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }
    [McpTool(FileToolNameConstants.FileSnipPreview, "Get file preview info (size, line count, first N lines)", "file", ConcurrencySafe = true)]
    public async Task<ToolResult> FileSnipPreviewAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
        [McpToolParameter("Max preview lines", Required = false, DefaultValue = "20")] int max_preview_lines = 20,
        CancellationToken cancellationToken = default)
    {
        if (_snipLogic == null)
        {
            var notInitDiag = BuildFileChunkingServiceNotInitializedDiagnostic();
            return ToolResultBuilder.Error().WithText(notInitDiag.FormattedMessage).WithDiagnostic(notInitDiag).Build();
        }

        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateRange(max_preview_lines, 1, int.MaxValue, "max_preview_lines"));
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        SnipPreview preview;
        try
        {
            preview = await _snipLogic.GetPreviewAsync(file_path, max_preview_lines, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            RecordFileMetrics(FileOperationType.SnipPreview, FileOperationResult.Failed);
            var diagnostic = FileSuggestionHelper.BuildFileNotFoundDiagnostic(file_path, _fs);
            return ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordFileMetrics(FileOperationType.SnipPreview, FileOperationResult.Failed);
            _logger?.LogError(ex, "SnipPreview 调用抛出异常: {FilePath}", file_path);
            var exDiagnostic = ToolDiagnostic.Create("SnipPreviewFailed",
                $"获取预览失败: {ex.Message}",
                [new DiagnosticDetail("filePath", file_path), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查文件权限、是否被其他进程锁定。"]);
            return ToolResultBuilder.Error().WithText(exDiagnostic.FormattedMessage).WithDiagnostic(exDiagnostic).Build();
        }

        var response = new StringBuilder(256);
        response.AppendLine($"File: {preview.FilePath}");
        response.AppendLine($"Size: {ContentReplacementConstants.FormatFileSize(preview.FileSize)}");
        response.AppendLine($"Total lines: {preview.TotalLines}");
        response.AppendLine();
        response.AppendLine("--- Preview ---");
        response.Append(preview.PreviewContent.TrimEnd());
        if (preview.TotalLines > max_preview_lines)
            response.AppendLine("...");

        RecordFileMetrics(FileOperationType.SnipPreview, FileOperationResult.Ok);
        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }
}
