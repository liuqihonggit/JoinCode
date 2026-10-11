namespace Tools.Handlers;

public partial class FileToolHandlers {
    /// <summary>删除指定文件，删除前自动备份以支持恢复</summary>
    [McpTool(FileToolNameEnumConstants.FileDelete, "Delete the specified file", "file")]
    public async Task<ToolResult> FileDeleteAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
        CancellationToken cancellationToken = default) {
        // ── 参数校验 ──
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(file_path, "file_path"),
            ValidationHelper.ValidateStringLength(file_path, 4096, "file_path"));
        if (validationError != null) {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        // ── 统一写入防御链 — 删除是销毁操作：UNC 拒绝 + 沙箱解析 + 删除前备份 ──
        // 不需要密钥检测（删除不引入内容）、写前读、脏写保护
        var safety = await _writeDefense
            .Begin(file_path, null, FileOperationType.Delete, "deleting")
            .Then(_writeDefense.RejectUncPath)           // UNC 路径拒绝
            .Then(_writeDefense.ResolveSandboxAsync)     // 沙箱路径解析
            .Then(_writeDefense.BackupBeforeWriteAsync)  // 删除前备份（支持恢复）
            .ExecuteAsync(cancellationToken).ConfigureAwait(false);

        if (safety.Rejection is not null) return safety.Rejection;
        file_path = safety.Context.ResolvedPath;

        if (!_fs.FileExists(file_path)) {
            RecordFileMetrics(FileOperationType.Delete, FileOperationResult.Failed);
            var diagnostic = FileSuggestionHelper.BuildFileNotFoundDiagnostic(file_path, _fs);
            return ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
        }

        var success = await _fileOperationService.DeleteFileAsync(
            file_path,
            cancellationToken).ConfigureAwait(false);

        if (!success) {
            RecordFileMetrics(FileOperationType.Delete, FileOperationResult.Failed);
            var deleteMsg = $"Failed to delete file: {file_path}\n[诊断] 文件存在但删除失败，可能被其他进程锁定或无删除权限。";
            return ToolResultBuilder.Error().WithText(deleteMsg)
                .WithDiagnostic(ToolDiagnostic.Create("DeleteFailed", deleteMsg,
                    [new DiagnosticDetail("filePath", file_path)],
                    ["文件可能被其他进程锁定或无删除权限。"])).Build();
        }

        // ── 统一写入后通知 ──
        _writeDefense.NotifyWriteComplete(file_path, null, "delete", FileOperationType.Delete);
        return ToolResultBuilder.Success().WithText($"File deleted: {file_path}").Build();
    }
}