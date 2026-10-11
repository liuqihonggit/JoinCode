namespace Core.Context;

/// <summary>
/// 输出循环检测器接口 — 检测累积输出文本是否存在重复模式循环
/// </summary>
public interface IOutputLoopDetector {
    /// <summary>
    /// 检测累积文本尾部是否存在重复模式循环，返回检测结果。
    /// </summary>
    LoopDetectionResult Detect(string accumulatedText);

    /// <summary>
    /// 检测累积文本（StringBuilder）尾部是否存在重复模式循环。
    /// 延迟 ToString() 调用 — 当检查间隔未满足时直接返回 NoLoop，避免 O(n) 字符串拷贝。
    /// </summary>
    LoopDetectionResult Detect(StringBuilder accumulatedText);

    /// <summary>
    /// 重置检测器内部状态，用于开始新一轮检测。
    /// </summary>
    void Reset();
}