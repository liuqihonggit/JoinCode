namespace JoinCode.Abstractions.Interfaces;

public interface IFileTransferService {
    /// <summary>异步发送文件并返回文件标识。</summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="description">描述（可选）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<string> SendFileAsync(string filePath, string? description = null, CancellationToken ct = default);
    /// <summary>异步生成文件下载链接。</summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="ct">取消令牌。</param>
    Task<string> GenerateDownloadLinkAsync(string filePath, CancellationToken ct = default);
}