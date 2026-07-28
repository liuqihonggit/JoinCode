namespace Core.Context;

public interface IOutputLoopDetector
{
    /// <summary>
    /// 检测累积文本尾部是否存在重复模式循环，返回检测结果。
    /// </summary>
    LoopDetectionResult Detect(string accumulatedText);

    /// <summary>
    /// 重置检测器内部状态，用于开始新一轮检测。
    /// </summary>
    void Reset();
}
