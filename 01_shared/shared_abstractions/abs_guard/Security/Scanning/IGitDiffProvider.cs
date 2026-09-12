namespace JoinCode.Abstractions.Security.Scanning;

public interface IGitDiffProvider
{
    Task<IReadOnlyList<string>> GetStagedFileNamesAsync(string workingDirectory, CancellationToken ct = default);

    Task<string> GetStagedDiffAsync(string workingDirectory, CancellationToken ct = default);
}
