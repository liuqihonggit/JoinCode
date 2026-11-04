namespace JoinCode.Abstractions.Security.Scanning;

public interface IGitDiffProvider {
    /// <summary>获取暂存文件名列表。</summary>
    Task<IReadOnlyList<string>> GetStagedFileNamesAsync(string workingDirectory, CancellationToken ct = default);

    /// <summary>获取暂存差异内容。</summary>
    Task<string> GetStagedDiffAsync(string workingDirectory, CancellationToken ct = default);
}