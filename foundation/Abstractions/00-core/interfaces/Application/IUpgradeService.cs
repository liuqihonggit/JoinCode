namespace JoinCode.Abstractions.Interfaces;

public interface IUpgradeService
{
    Version GetCurrentVersion();
    Task<Version?> GetLatestVersionAsync(CancellationToken ct = default);
    Task<bool> IsUpdateAvailableAsync(CancellationToken ct = default);
}
