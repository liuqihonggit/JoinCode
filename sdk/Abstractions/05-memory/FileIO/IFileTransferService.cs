namespace JoinCode.Abstractions.Interfaces;

public interface IFileTransferService
{
    Task<string> SendFileAsync(string filePath, string? description = null, CancellationToken ct = default);
    Task<string> GenerateDownloadLinkAsync(string filePath, CancellationToken ct = default);
}
