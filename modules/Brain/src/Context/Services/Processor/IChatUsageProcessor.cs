namespace Core.Context;

/// <summary>
/// 用量处理器接口 — 成本计算、缓存检测
/// </summary>
public interface IChatUsageProcessor
{
    /// <summary>
    /// 处理用量 + 缓存检测 + 上下文折叠
    /// </summary>
    Task ProcessUsageAsync(TokenUsage usage, string? modelId, PromptStateSnapshot promptSnapshot, CancellationToken ct);

    /// <summary>
    /// 从流式响应元数据中提取费率限制数据
    /// </summary>
    void TryExtractRateLimitData(IReadOnlyDictionary<string, JsonElement> metadata);
}
