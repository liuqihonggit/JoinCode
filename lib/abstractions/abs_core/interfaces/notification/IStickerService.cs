namespace JoinCode.Abstractions.Interfaces;

/// <summary>贴纸服务接口。</summary>
public interface IStickerService {
    /// <summary>异步打开贴纸页面。</summary>
    Task<bool> OpenStickerPageAsync(CancellationToken ct = default);
    /// <summary>获取贴纸页面 URL。</summary>
    string GetStickerPageUrl();
}
