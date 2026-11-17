namespace JoinCode.Abstractions.Security.Scanning;

public interface IGitSecurityInterceptor {
    /// <summary>异步在提交前扫描工作目录。</summary>
    Task<ScanResult> ScanBeforeCommitAsync(string workingDirectory, CancellationToken ct = default);
}