
namespace Services.Api.Vcr;

public interface IVcrService
{
    Task<VcrCassette> LoadCassetteAsync(string name, CancellationToken cancellationToken = default);
    Task SaveCassetteAsync(VcrCassette cassette, CancellationToken cancellationToken = default);
    Task RecordInteractionAsync(string cassetteName, VcrRequest request, VcrResponse response, CancellationToken cancellationToken = default);
    Task<VcrResponse?> FindMatchingInteractionAsync(string cassetteName, VcrRequest request, CancellationToken cancellationToken = default);
    VcrMode CurrentMode { get; }
    void SetMode(VcrMode mode);
}
