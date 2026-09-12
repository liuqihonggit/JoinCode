namespace JoinCode.Abstractions.CodeIndex;

public interface ISymbolIndex
{
    Task IndexFileAsync(string filePath, CancellationToken ct);
    Task IndexFilesAsync(IReadOnlyList<string> filePaths, CancellationToken ct);
    Task RemoveFileAsync(string filePath, CancellationToken ct);
    Task ClearAsync(CancellationToken ct);
    Task<IndexStats> GetStatsAsync(CancellationToken ct);
}
