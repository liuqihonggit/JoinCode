namespace Tools.Handlers;

public partial class FileToolHandlers
{
    [McpTool(FileToolNameConstants.FileWrite, "Write a file to the local filesystem", "file")]
    public async Task<ToolResult> FileWriteAsync(
        [McpToolParameter("The absolute path to the file to write (must be absolute, not relative)")] string file_path,
        [McpToolParameter("The content to write to the file")] string content,
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

        // ── 统一写入防御链 — 每步有名函数，链式编排，任一失败短路 ──
        var safety = await WriteDefense
            .Begin(file_path, content, FileOperationType.Write, "writing")
            .Then(RejectUncPath)           // UNC 路径拒绝（防凭据泄露）
            .Then(ResolveSandboxAsync)     // 沙箱路径解析
            .Then(CheckTeamMemSecrets)     // 团队密钥检测
            .Then(RequireReadBeforeWrite)  // 写前读校验
            .Then(GuardStaleWriteAsync)    // 脏写保护
            .Then(BackupBeforeWriteAsync)  // 写前备份
            .ExecuteAsync(cancellationToken).ConfigureAwait(false);

        if (safety.Rejection is not null) return safety.Rejection;
        file_path = safety.Context.ResolvedPath;

        // ── 执行写入 ──
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

        // ── 构建成功响应 ──
        var response = result.Operation == FileOperationTypeConstants.Create
            ? $"File created successfully at: {result.FilePath}"
            : $"The file {result.FilePath} has been updated successfully.";

        // 附加 structuredPatch 到 ToolResult — 对齐 TS FileWriteTool 返回 structuredPatch
        var toolResult = ToolResultBuilder.Success().WithText(response).Build();
        if (result.StructuredPatch.Any())
        {
            toolResult.StructuredPatch = result.StructuredPatch.ToArray();
        }

        // ── 统一写入后通知 — LSP 诊断清除 + LSP 文件变更 + 遥测 + 写入监听器 ──
        NotifyWriteComplete(result.FilePath, content, "write", FileOperationType.Write);
        return toolResult;
    }
}
