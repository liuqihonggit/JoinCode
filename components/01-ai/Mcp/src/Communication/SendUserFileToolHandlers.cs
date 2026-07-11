

namespace McpToolHandlers;

[McpToolHandler(ToolCategory.FileTransfer, Optional = true)]
public partial class SendUserFileToolHandlers
{
    [Inject] private readonly ILogger<SendUserFileToolHandlers>? _logger;
    private readonly IFileTransferService? _transferService;
    private readonly IFileSystem _fs;

    public SendUserFileToolHandlers(IFileSystem fs, ILogger<SendUserFileToolHandlers>? logger = null, IFileTransferService? transferService = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        _logger = logger;
        _transferService = transferService;
        _fs = fs;
    }

    [McpTool(SystemToolNameConstants.SendUserFile, "Send file for user to view or download", "file")]
    public async Task<ToolResult> SendUserFileAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Send description (optional)", Required = false)] string? description = null,
        [McpToolParameter("Preview in terminal (optional, default true)", Required = false)] bool? preview = true,
        [McpToolParameter("Generate download link (optional, default false)", Required = false)] bool? generate_link = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
            return McpResultBuilder.Error().WithText(L.T(StringKey.SendUserFilePathCannotBeEmpty)).Build();

        try
        {
            if (!_fs.FileExists(file_path))
                return McpResultBuilder.Error().WithText(L.T(StringKey.SendUserFileNotFound, file_path)).Build();

            long fileSize;
            using (var sizeStream = _fs.OpenRead(file_path))
            {
                fileSize = sizeStream.Length;
            }
            var lastWriteTime = _fs.GetLastWriteTime(file_path);
            var response = new System.Text.StringBuilder();

            if (_transferService != null)
            {
                var transferResult = await _transferService.SendFileAsync(file_path, description, cancellationToken).ConfigureAwait(false);
                response.AppendLine(transferResult);
            }
            else
            {
                response.AppendLine(L.T(StringKey.SendUserFileSent));
                response.AppendLine(L.T(StringKey.SendUserFileLabelPath, file_path));
                response.AppendLine(L.T(StringKey.SendUserFileLabelSize, ContentReplacementConstants.FormatFileSize(fileSize)));
                response.AppendLine(L.T(StringKey.SendUserFileLabelModifiedTime, lastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")));

                if (!string.IsNullOrEmpty(description))
                    response.AppendLine(L.T(StringKey.SendUserFileLabelDescription, description));
            }

            if (generate_link == true && _transferService != null)
            {
                try
                {
                    var link = await _transferService.GenerateDownloadLinkAsync(file_path, cancellationToken).ConfigureAwait(false);
                    response.AppendLine();
                    response.AppendLine(link);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "{Message}", L.T(StringKey.SendUserFileDownloadLinkFailedLog));
                    response.AppendLine();
                    response.AppendLine(L.T(StringKey.SendUserFileDownloadLinkFailed));
                }
            }

            if (preview == true && fileSize < 10240)
            {
                response.AppendLine();
                response.AppendLine(L.T(StringKey.SendUserFileContentPreview));
                var content = await _fs.ReadAllTextAsync(file_path, cancellationToken).ConfigureAwait(false);
                var lines = content.Split('\n');
                var displayLines = lines.Take(50);
                response.Append(string.Join("\n", displayLines));
                if (lines.Length > 50)
                    response.AppendLine($"\n{L.T(StringKey.SendUserFilePreviewLineCount, lines.Length.ToString())}");
                response.AppendLine();
                response.AppendLine(L.T(StringKey.SendUserFilePreviewEnd));
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Message}", L.T(StringKey.SendUserFileFailedLog, file_path));
            return McpResultBuilder.Error().WithText(L.T(StringKey.SendUserFileFailed, ex.Message)).Build();
        }
    }
}
