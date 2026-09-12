namespace JoinCode.Abstractions.Interfaces;

public sealed class ReleaseInfo
{
    public required string Version { get; init; }
    public required string Notes { get; init; }
    public required DateTime PublishedAt { get; init; }
}

public interface IReleaseNotesService
{
    Task<IReadOnlyList<ReleaseInfo>> GetRecentReleasesAsync(int count = 5, CancellationToken ct = default);
}
