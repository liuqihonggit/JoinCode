namespace JoinCode.Abstractions.Models.Agent;

public sealed class SubAgentResult
{
    public required string AgentId { get; init; }
    public bool IsSuccess { get; init; }
    public required string Output { get; init; }
    public string? Error { get; init; }
    public long? ExecutionTimeMs { get; init; }
    public CacheSafeParams? CacheSafeParams { get; init; }

    /// <summary>L0 一句话概要（来自 SubAgentOutputEnvelope.ExtractSummary）</summary>
    public string? Summary { get; init; }

    /// <summary>L3 落盘路径（null=未落盘，在预算内原样返回）</summary>
    public string? ArchivedPath { get; init; }

    /// <summary>true=走了 L2 自摘要或 L3 落盘；false=L1 原样返回</summary>
    public bool IsCompacted { get; init; }
}
