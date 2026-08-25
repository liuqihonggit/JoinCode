namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// UI 元素 — 多模态 LLM 从截图识别的界面元素，含坐标/状态/语义描述
/// </summary>
public sealed record UiElement(
    UiElementType Type,
    string? Text,
    string? Description,
    int X,
    int Y,
    int Width,
    int Height,
    ElementState State,
    double Confidence);

/// <summary>
/// UI 元素检测结果 — 含元素列表与截图尺寸（用于坐标校验）
/// </summary>
public sealed record UiElementDetectionResult(
    IReadOnlyList<UiElement> Elements,
    int ImageWidth,
    int ImageHeight);
