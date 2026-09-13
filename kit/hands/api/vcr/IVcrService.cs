
namespace Services.Api.Vcr;

public interface IVcrService
{
    string GetCassettePath(string name, string? directory = null);
    Task<VcrCassette> LoadCassetteAsync(string name, string? directory = null, CancellationToken cancellationToken = default);
    Task SaveCassetteAsync(VcrCassette cassette, string? directory = null, CancellationToken cancellationToken = default);
    Task RecordInteractionAsync(string cassetteName, VcrRequest request, VcrResponse response, string? directory = null, CancellationToken cancellationToken = default);
    Task<VcrResponse?> FindMatchingInteractionAsync(string cassetteName, VcrRequest request, string? directory = null, CancellationToken cancellationToken = default);
    VcrMode CurrentMode { get; }
    string CassettesDirectory { get; }
    void SetMode(VcrMode mode);
}
