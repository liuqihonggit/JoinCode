namespace JoinCode.Abstractions.Interfaces;

public interface IChromeIntegrationService {
    /// <summary>获取扩展是否已安装。</summary>
    bool IsExtensionInstalled { get; }
    /// <summary>异步连接 Chrome 扩展。</summary>
    /// <param name="ct">取消令牌。</param>
    Task<bool> ConnectAsync(CancellationToken ct = default);
    /// <summary>异步断开连接。</summary>
    /// <param name="ct">取消令牌。</param>
    Task DisconnectAsync(CancellationToken ct = default);
    /// <summary>获取是否已连接。</summary>
    bool IsConnected { get; }
    /// <summary>打开扩展页面。</summary>
    /// <param name="ct">取消令牌。</param>
    Task OpenExtensionPageAsync(CancellationToken ct = default);
    /// <summary>切换默认启用状态。</summary>
    /// <param name="ct">取消令牌。</param>
    Task<bool> ToggleDefaultEnabledAsync(CancellationToken ct = default);
    /// <summary>获取是否默认启用。</summary>
    bool IsDefaultEnabled { get; }
}
