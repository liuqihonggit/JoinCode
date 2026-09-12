namespace JoinCode.Abstractions.Security.Scanning;

public interface IGitSecurityInterceptor
{
    int Priority { get; }

    Task<ScanResult> ScanBeforeCommitAsync(string workingDirectory, CancellationToken ct = default);
}
