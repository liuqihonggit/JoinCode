namespace JoinCode.Abstractions.CodeIndex;

public interface ISymbolIndex {
    /// <summary>异步索引单个文件。</summary>
    Task IndexFileAsync(string filePath, CancellationToken ct);
    /// <summary>异步索引多个文件。</summary>
    Task IndexFilesAsync(IReadOnlyList<string> filePaths, CancellationToken ct);
    /// <summary>异步移除文件索引。</summary>
    Task RemoveFileAsync(string filePath, CancellationToken ct);
    /// <summary>异步清空索引。</summary>
    Task ClearAsync(CancellationToken ct);
    /// <summary>异步获取索引统计。</summary>
    Task<IndexStats> GetStatsAsync(CancellationToken ct);
}