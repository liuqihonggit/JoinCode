
namespace JoinCode.Abstractions.Interfaces;

public interface IVcrService {
    string GetCassettePath(string name, string? directory = null);
    string CassettesDirectory { get; }
    Task<VcrCassette> LoadCassetteAsync(string name, string? directory = null, CancellationToken cancellationToken = default);
    VcrMode CurrentMode { get; }
    void SetMode(VcrMode mode);
}