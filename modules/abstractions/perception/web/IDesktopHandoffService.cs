namespace JoinCode.Abstractions.Interfaces;

public interface IDesktopHandoffService {
    /// <summary>异步将会话移交到桌面端。</summary>
    Task<bool> HandoffToDesktopAsync(string sessionId, CancellationToken ct = default);
    /// <summary>获取一个值，指示桌面端是否可用。</summary>
    bool IsDesktopAvailable { get; }
    /// <summary>获取桌面连接信息。</summary>
    string? DesktopConnectionInfo { get; }
}