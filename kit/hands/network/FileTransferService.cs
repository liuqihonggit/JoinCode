namespace IO.Services;

/// <summary>文件传输服务 — 校验本地文件存在性后生成发送摘要与本地下载链接，供移动端或对等端消费。</summary>
[Register(typeof(IFileTransferService), ServiceLifetime.Singleton)]
public sealed partial class FileTransferService : ServiceEntity, IFileTransferService {
    private readonly ILogger<FileTransferService>? _logger;
    private readonly IFileSystem _fs;

    /// <summary>构造文件传输服务实例。</summary>
    /// <param name="fs">用于访问本地文件系统的抽象。</param>
    /// <param name="logger">可选的日志记录器，传入 null 时静默运行。</param>
    public FileTransferService(IFileSystem fs, ILogger<FileTransferService>? logger = null) {
        _fs = fs;
        _logger = logger;
    }

    /// <summary>异步发送指定文件，生成包含文件名、路径、大小与修改时间的摘要文本。</summary>
    /// <param name="filePath">要发送的本地文件路径。</param>
    /// <param name="description">可选的附加说明，非空时追加到摘要末尾。</param>
    /// <param name="ct">可取消令牌。</param>
    /// <returns>描述发送结果的文本摘要。</returns>
    public async Task<string> SendFileAsync(string filePath, string? description = null, CancellationToken ct = default) {
        if (!_fs.FileExists(filePath))
            throw new FileNotFoundException($"[HND005] 文件不存在: {filePath}");

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

    /// <summary>异步为指定文件生成本地下载链接，并返回包含链接与文件信息的文本。</summary>
    /// <param name="filePath">要生成下载链接的本地文件路径。</param>
    /// <param name="ct">可取消令牌。</param>
    /// <returns>包含下载链接与文件信息的文本描述。</returns>
    public async Task<string> GenerateDownloadLinkAsync(string filePath, CancellationToken ct = default) {
        if (!_fs.FileExists(filePath))
            throw new FileNotFoundException($"[HND006] 文件不存在: {filePath}");

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