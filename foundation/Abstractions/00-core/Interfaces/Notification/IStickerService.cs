namespace JoinCode.Abstractions.Interfaces;

public interface IStickerService
{
    Task<bool> OpenStickerPageAsync(CancellationToken ct = default);
    string GetStickerPageUrl();
}
