namespace JoinCode.Reasoning.Compression;

/// <summary>
/// 推理上下文压缩器 — 在 Agent 调用 LLM 前压缩 ReasoningContext
/// </summary>
public interface IReasoningContextCompressor
{
    /// <summary>
    /// 为指定角色构建压缩后的 Prompt 输入
    /// 策略：视锥过滤 → 超限截断 → IContextCompressor 压缩
    /// </summary>
    Task<CompressedPrompt> CompressForRoleAsync(
        ReasoningContext context,
        AgentRole role,
        int maxPromptTokens,
        CancellationToken ct = default);

    /// <summary>
    /// 对 DAG 中已裁决/驳回的节点生成摘要，替代原文
    /// </summary>
    Task SummarizeResolvedNodesAsync(Dag<ReasoningPayload> dag, int threshold = 30, CancellationToken ct = default);
}

/// <summary>
/// 压缩后的 Prompt 输入
/// </summary>
public sealed class CompressedPrompt
{
    public required string UserPrompt { get; init; }
    public required int EstimatedTokens { get; init; }
    public required CompressionMethod Method { get; init; }
    public required double OriginalTokenEstimate { get; init; }
    public string? CompressionSummary { get; init; }
}

/// <summary>
/// 压缩方式
/// </summary>
public enum CompressionMethod
{
    None,
    ConeFiltered,
    Truncated,
    LlmCompressed,
}
