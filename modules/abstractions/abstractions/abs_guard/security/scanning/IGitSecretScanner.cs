namespace JoinCode.Abstractions.Security.Scanning;

public interface IGitSecretScanner {
    /// <summary>异步扫描暂存文件名中是否包含敏感信息。</summary>
    Task<ScanResult> ScanFileNamesAsync(IReadOnlyList<string> stagedFiles, CancellationToken ct = default);

    /// <summary>异步扫描 diff 内容中是否包含敏感信息。</summary>
    Task<ScanResult> ScanContentAsync(string diffOutput, CancellationToken ct = default);
}
