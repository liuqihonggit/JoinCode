namespace Tools.Handlers;

public partial class FileToolHandlers
{
    [McpTool(FileToolNameConstants.DirectoryList, "List directory contents including files and subdirectories", "file", ConcurrencySafe = true)]
    public async Task<ToolResult> DirectoryListAsync(
        [McpToolParameter("Directory path, relative or absolute")] string directory_path,
        [McpToolParameter("Recursively list subdirectory contents, default false", Required = false, DefaultValue = "false")] bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(directory_path, "directory_path"),
            ValidationHelper.ValidateStringLength(directory_path, 4096, "directory_path"));
        if (validationError != null)
        {
            var validationDiag = BuildValidationErrorDiagnostic(validationError);
            return ToolResultBuilder.Error().WithText(validationDiag.FormattedMessage).WithDiagnostic(validationDiag).Build();
        }

        directory_path = await ResolveSandboxPathAsync(directory_path, cancellationToken).ConfigureAwait(false);

        var result = await _fileOperationService.ListDirectoryAsync(
            directory_path,
            recursive,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordFileMetrics(FileOperationType.List, FileOperationResult.Failed);
            var listDiag = BuildListDirectoryFailedDiagnostic(result.ErrorMessage ?? "Failed to list directory");
            return ToolResultBuilder.Error().WithText(listDiag.FormattedMessage).WithDiagnostic(listDiag).Build();
        }

        var response = new StringBuilder(512);
        response.AppendLine($"Directory: {result.DirectoryPath}");
        response.AppendLine($"Subdirectories: {result.Directories.Count}");
        response.AppendLine($"Files: {result.Files.Count}");

        if (result.Directories.Count > 0)
        {
            response.AppendLine();
            response.AppendLine("[Subdirectories]");
            foreach (var dir in result.Directories.Take(50))
            {
                response.AppendLine($"  {ObjectSymbol.Directory.ToValue()} {dir.Name}/");
            }
            if (result.Directories.Count > 50)
            {
                response.AppendLine($"  ... and {result.Directories.Count - 50} more subdirectories");
            }
        }

        if (result.Files.Count > 0)
        {
            response.AppendLine();
            response.AppendLine("[Files]");
            foreach (var file in result.Files.Take(100))
            {
                var size = ContentReplacementConstants.FormatFileSize(file.Size);
                response.AppendLine($"  {ObjectSymbol.File.ToValue()} {file.Name} ({size})");
            }
            if (result.Files.Count > 100)
            {
                response.AppendLine($"  ... and {result.Files.Count - 100} more files");
            }
        }

        RecordFileMetrics(FileOperationType.List, FileOperationResult.Ok);
        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }
}
