namespace JoinCode.Reasoning.Engine;

/// <summary>
/// DAG 节点载荷 — 联合体：假定/证据/裁决共用一个类型
/// </summary>
public sealed class ReasoningPayload
{
    public required string Id { get; init; }
    public required ReasoningNodeType Type { get; init; }
    public required string Content { get; set; }
    /// <summary>
    /// 原始内容（压缩前的完整文本，仅在压缩后非空）
    /// </summary>
    public string? OriginalContent { get; set; }
    public DataState State { get; set; } = DataState.Assumption;
    public int Confidence { get; set; } = 50;
    public EvidenceCategory? Category { get; init; }
    public TrustLevel? TrustLevel { get; set; }
    public AgentRole? SubmittedBy { get; init; }
    public string? Source { get; init; }
    public string? SourceUrl { get; init; }
    public double Weight { get; init; } = 1.0;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? VerifiedAt { get; set; }
    public string? VerifiedBy { get; set; }
}

/// <summary>
/// 推理节点类型
/// </summary>
public enum ReasoningNodeType
{
    [EnumValue("assumption")] Assumption,
    [EnumValue("evidence")] Evidence,
    [EnumValue("verdict")] Verdict,
}
