namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 环境感知服务 — 弹窗检测/光标状态/异步等待（PRD E-01/E-03）
/// </summary>
public interface IEnvironmentAwarenessService
{
    /// <summary>检测当前是否有非预期弹窗（E-01）</summary>
    /// <returns>弹窗信息，无弹窗返回 null</returns>
    Task<PopupInfo?> DetectPopupAsync(CancellationToken cancellationToken = default);

    /// <summary>获取当前光标状态（E-03）— 沙漏=异步操作进行中</summary>
    Task<CursorState> GetCursorStateAsync(CancellationToken cancellationToken = default);

    /// <summary>等待异步操作完成（E-03）— 光标恢复 Normal 或超时</summary>
    /// <param name="timeout">最大等待时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>true=已恢复空闲，false=超时</returns>
    Task<bool> WaitForIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
