namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// UI 元素检测器 — 截图 → 多模态 LLM → 结构化 UI 元素列表（PRD V-02/V-03/V-04）
/// </summary>
public interface IUiElementDetector
{
    /// <summary>检测截图中的所有 UI 元素（V-02 + V-03）</summary>
    /// <param name="base64Png">base64 编码的 PNG 截图</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>UI 元素列表（含类型/坐标/状态/语义描述）</returns>
    Task<UiElementDetectionResult> DetectAsync(string base64Png, CancellationToken cancellationToken = default);

    /// <summary>按语义描述查找元素（V-04）— 如"红色的停止按钮"</summary>
    /// <param name="base64Png">base64 PNG 截图</param>
    /// <param name="description">语义描述（自然语言）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配度最高的元素，未找到返回 null</returns>
    Task<UiElement?> FindByDescriptionAsync(string base64Png, string description, CancellationToken cancellationToken = default);
}
