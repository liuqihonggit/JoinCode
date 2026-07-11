namespace JoinCode.Abstractions.Interfaces;

public interface IDesktopHandoffService
{
    Task<bool> HandoffToDesktopAsync(string sessionId, CancellationToken ct = default);
    bool IsDesktopAvailable { get; }
    string? DesktopConnectionInfo { get; }
}
