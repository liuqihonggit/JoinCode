namespace JoinCode.Abstractions.Models.Agent;

public sealed class SubAgentResult {
    /// <summary>获取子代理标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取一个值，指示执行是否成功。</summary>
    public bool IsSuccess { get; init; }
    /// <summary>获取输出内容。</summary>
    public required string Output { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? Error { get; init; }
    /// <summary>获取执行时长（毫秒）。</summary>
    public long? ExecutionTimeMs { get; init; }
    /// <summary>获取缓存安全参数。</summary>
    public CacheSafeParams? CacheSafeParams { get; init; }

    /// <summary>L0 一句话概要（来自 SubAgentOutputEnvelope.ExtractSummary）</summary>
    public string? Summary { get; init; }

    /// <summary>L3 落盘路径（null=未落盘，在预算内原样返回）</summary>
    public string? ArchivedPath { get; init; }

    /// <summary>true=走了 L2 自摘要或 L3 落盘；false=L1 原样返回</summary>
    public bool IsCompacted { get; init; }
}