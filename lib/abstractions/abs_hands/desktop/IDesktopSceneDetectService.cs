namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 桌面场景 UI 元素检测服务 — 多模态识别当前区域的 UI 元素
/// </summary>
public interface IDesktopSceneDetectService
{
    /// <summary>识别当前缩放区域的 UI 元素（按钮/输入框/标签等）</summary>
    /// <param name="sceneId">场景 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<DesktopSceneDetection> DetectAsync(string sceneId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 桌面场景检测结果
/// </summary>
/// <param name="SceneId">场景 ID</param>
/// <param name="Elements">识别到的 UI 元素列表</param>
public sealed record DesktopSceneDetection(string SceneId, IReadOnlyList<DetectedUiElement> Elements);

/// <summary>
/// 检测到的 UI 元素
/// </summary>
/// <param name="Type">元素类型: button/input/label/link</param>
/// <param name="Label">元素文字标签</param>
/// <param name="CellCode">所在格子编码</param>
public sealed record DetectedUiElement(string Type, string Label, string CellCode);
