namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 屏幕截图采集服务 — GDI BitBlt 封装，返回 base64 编码的 PNG
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>全屏截图，返回 base64 PNG</summary>
    Task<string> CaptureFullScreenAsync(CancellationToken cancellationToken = default);

    /// <summary>指定窗口客户区截图，返回 base64 PNG</summary>
    Task<string> CaptureWindowAsync(IntPtr hWnd, CancellationToken cancellationToken = default);

    /// <summary>指定屏幕区域截图，返回 base64 PNG</summary>
    Task<string> CaptureRegionAsync(int x, int y, int width, int height, CancellationToken cancellationToken = default);
}
