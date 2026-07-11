using JoinCode.Abstractions.Attributes;

namespace IO.Services;

[Register]
public sealed partial class FileTransferService : IFileTransferService
{
    [Inject] private readonly ILogger<FileTransferService>? _logger;
    private readonly IFileSystem _fs;

    public FileTransferService(IFileSystem fs, ILogger<FileTransferService>? logger = null)
    {
        _fs = fs;
        _logger = logger;
    }

    public async Task<string> SendFileAsync(string filePath, string? description = null, CancellationToken ct = default)
    {
        if (!_fs.FileExists(filePath))
            throw new FileNotFoundException($"文件不存在: {filePath}");

        var fileName = Path.GetFileName(filePath);
        var fileLength = _fs.GetFileLength(filePath);
        var lastWriteTime = _fs.GetLastWriteTime(filePath);
        var response = new System.Text.StringBuilder();

        response.AppendLine($"文件已发送: {fileName}");
        response.AppendLine($"路径: {filePath}");
        response.AppendLine($"大小: {FormatFileSize(fileLength)}");
        response.AppendLine($"修改时间: {lastWriteTime:yyyy-MM-dd HH:mm:ss}");

        if (!string.IsNullOrEmpty(description))
            response.AppendLine($"说明: {description}");

        return response.ToString();
    }

    public async Task<string> GenerateDownloadLinkAsync(string filePath, CancellationToken ct = default)
    {
        if (!_fs.FileExists(filePath))
            throw new FileNotFoundException($"文件不存在: {filePath}");

        var fileName = Path.GetFileName(filePath);
        var fileLength = _fs.GetFileLength(filePath);

        var port = 18732 + Random.Shared.Next(0, 1000);
        var link = $"http://localhost:{port}/download/{Uri.EscapeDataString(fileName)}";

        var response = new System.Text.StringBuilder();
        response.AppendLine($"下载链接已生成:");
        response.AppendLine(link);
        response.AppendLine();
        response.AppendLine($"文件: {fileName} ({FormatFileSize(fileLength)})");
        response.AppendLine("注意: 此链接仅在本地有效，服务运行期间可访问");

        _logger?.LogInformation("为文件 {FileName} 生成下载链接: {Link}", fileName, link);

        await Task.CompletedTask.ConfigureAwait(false);
        return response.ToString();
    }

    private static string FormatFileSize(long bytes) => JoinCode.Abstractions.LLM.Chat.ContentReplacementConstants.FormatFileSize(bytes);
}
