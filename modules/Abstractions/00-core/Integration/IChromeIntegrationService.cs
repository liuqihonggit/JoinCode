namespace JoinCode.Abstractions.Interfaces;

public interface IChromeIntegrationService
{
    bool IsExtensionInstalled { get; }
    Task<bool> ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    bool IsConnected { get; }
    Task OpenExtensionPageAsync(CancellationToken ct = default);
    Task<bool> ToggleDefaultEnabledAsync(CancellationToken ct = default);
    bool IsDefaultEnabled { get; }
}
