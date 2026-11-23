namespace Core.Context;

/// <summary>
/// 循环干预结果 — QueryLoop 中检测到循环时返回，触发 LoopDetected 事件
/// 与 Loop/LoopDetectionResult（输出循环检测）语义不同，此类型用于工具调用序列与逻辑指纹循环
/// </summary>
/// <param name="TriggerCount">累计触发次数</param>
/// <param name="ToolCallCount">工具调用次数</param>
/// <param name="Reason">触发原因描述</param>
public sealed record LoopInterventionResult(int TriggerCount, int ToolCallCount, string Reason);

/// <summary>
/// 循环检测策略接口 — 文本循环检测和工具调用序列循环检测
/// </summary>
public interface ILoopDetectionStrategy {
    /// <summary>
    /// 尝试检测文本响应的逻辑指纹循环
    /// </summary>
    /// <param name="text">响应文本</param>
    /// <param name="result">检测到循环时的干预结果</param>
    /// <returns>检测到循环返回 true</returns>
    bool TryGetTextLoop(string text, [NotNullWhen(true)] out LoopInterventionResult? result);

    /// <summary>
    /// 尝试检测工具调用序列循环
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">工具参数</param>
    /// <param name="result">检测到循环时的干预结果</param>
    /// <returns>检测到循环返回 true</returns>
    bool TryGetToolCallLoop(string toolName, Dictionary<string, JsonElement>? arguments, [NotNullWhen(true)] out LoopInterventionResult? result);
}