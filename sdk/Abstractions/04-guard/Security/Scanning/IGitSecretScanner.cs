namespace JoinCode.Abstractions.Security.Scanning;

public interface IGitSecretScanner
{
    Task<ScanResult> ScanFileNamesAsync(IReadOnlyList<string> stagedFiles, CancellationToken ct = default);

    Task<ScanResult> ScanContentAsync(string diffOutput, CancellationToken ct = default);
}
