namespace Core.Context;

/// <summary>
/// 用量处理器接口 — 成本计算、缓存检测
/// </summary>
public interface IChatUsageProcessor
{
    /// <summary>
    /// 处理用量 + 缓存检测 + 上下文折叠
    /// </summary>
    /// <param name="usage">LLM 响应的 token 用量。</param>
    /// <param name="modelId">模型标识。</param>
    /// <param name="promptSnapshot">请求前记录的前缀状态快照。</param>
    /// <param name="agentId">代理标识，null 表示主代理。需与 RecordPromptStateAsync 使用相同的 agentId。</param>
    /// <param name="ct">取消令牌。</param>
    Task ProcessUsageAsync(TokenUsage usage, string? modelId, PromptStateSnapshot promptSnapshot, string? agentId = null, CancellationToken ct = default);

    /// <summary>
    /// 从流式响应元数据中提取费率限制数据
    /// </summary>
    void TryExtractRateLimitData(IReadOnlyDictionary<string, JsonElement> metadata);
}
