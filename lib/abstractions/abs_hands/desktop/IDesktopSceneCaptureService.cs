namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 桌面场景截图编排服务 — 截图 + 四叉树网格构建 + 网格叠加渲染的一体化封装
/// </summary>
public interface IDesktopSceneCaptureService
{
    /// <summary>全屏截图并构建四叉树网格叠加渲染图</summary>
    /// <param name="depth">四叉树层数（1=4格, 2=16格），默认 2</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>截图 + 渲染图 + 尺寸信息</returns>
    Task<DesktopSceneCapture> CaptureWithGridAsync(int depth = 2, CancellationToken cancellationToken = default);
}

/// <summary>
/// 桌面场景截图结果 — 含原始截图、网格叠加渲染图、图片尺寸
/// </summary>
/// <param name="ScreenshotBase64">原始截图 base64 PNG</param>
/// <param name="RenderedBase64">带四叉树网格标注的渲染图 base64 PNG</param>
/// <param name="ImageWidth">图片宽度（像素）</param>
/// <param name="ImageHeight">图片高度（像素）</param>
/// <param name="Depth">四叉树层数</param>
public sealed record DesktopSceneCapture(
    string ScreenshotBase64,
    string RenderedBase64,
    int ImageWidth,
    int ImageHeight,
    int Depth);
