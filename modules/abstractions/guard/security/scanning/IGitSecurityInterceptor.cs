namespace JoinCode.Abstractions.Security.Scanning;

public interface IGitSecurityInterceptor {
    /// <summary>获取拦截器优先级。</summary>
    int Priority { get; }

    /// <summary>异步在提交前扫描工作目录。</summary>
    Task<ScanResult> ScanBeforeCommitAsync(string workingDirectory, CancellationToken ct = default);
}