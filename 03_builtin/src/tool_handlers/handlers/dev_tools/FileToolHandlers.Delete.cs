namespace Tools.Handlers;

public partial class FileToolHandlers
{
    [McpTool(FileToolNameConstants.FileDelete, "Delete the specified file", "file")]
    public async Task<ToolResult> FileDeleteAsync(
        [McpToolParameter("File path, relative or absolute")] string file_path,
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

        file_path = await ResolveSandboxPathAsync(file_path, cancellationToken).ConfigureAwait(false);

        if (!_fs.FileExists(file_path))
        {
            RecordFileMetrics(FileOperationType.Delete, FileOperationResult.Failed);
            var diagnostic = FileSuggestionHelper.BuildFileNotFoundDiagnostic(file_path, _fs);
            return ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
        }

        var success = await _fileOperationService.DeleteFileAsync(
            file_path,
            cancellationToken).ConfigureAwait(false);

        if (!success)
        {
            RecordFileMetrics(FileOperationType.Delete, FileOperationResult.Failed);
            var deleteMsg = $"Failed to delete file: {file_path}\n[诊断] 文件存在但删除失败，可能被其他进程锁定或无删除权限。";
            return ToolResultBuilder.Error().WithText(deleteMsg)
                .WithDiagnostic(ToolDiagnostic.Create("DeleteFailed", deleteMsg,
                    [new DiagnosticDetail("filePath", file_path)],
                    ["文件可能被其他进程锁定或无删除权限。"])).Build();
        }

        RecordFileMetrics(FileOperationType.Delete, FileOperationResult.Ok);
        NotifyFileWrite(file_path, "delete");
        return ToolResultBuilder.Success().WithText($"File deleted: {file_path}").Build();
    }
}
