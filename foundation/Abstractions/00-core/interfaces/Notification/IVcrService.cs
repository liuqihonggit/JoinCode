
namespace JoinCode.Abstractions.Interfaces;

public interface IVcrService
{
    Task<VcrCassette> LoadCassetteAsync(string name, CancellationToken cancellationToken = default);
    VcrMode CurrentMode { get; }
    void SetMode(VcrMode mode);
}
