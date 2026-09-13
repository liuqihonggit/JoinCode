namespace JoinCode.Reasoning.Engine;

/// <summary>
/// DAG 节点载荷 — 联合体：假定/证据/裁决共用一个类型
/// </summary>
public sealed class ReasoningPayload
{
    /// <summary>
    /// 节点唯一标识
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// 节点类型（假定/证据/裁决）
    /// </summary>
    public required ReasoningNodeType Type { get; init; }
    /// <summary>
    /// 节点内容文本
    /// </summary>
    public required string Content { get; set; }
    /// <summary>
    /// 原始内容（压缩前的完整文本，仅在压缩后非空）
    /// </summary>
    public string? OriginalContent { get; set; }
    /// <summary>
    /// 数据状态
    /// </summary>
    public DataState State { get; set; } = DataState.Assumption;
    /// <summary>
    /// 置信度（0-100）
    /// </summary>
    public int Confidence { get; set; } = 50;
    /// <summary>
    /// 证据分类
    /// </summary>
    public EvidenceCategory? Category { get; init; }
    /// <summary>
    /// 信任等级
    /// </summary>
    public TrustLevel? TrustLevel { get; set; }
    /// <summary>
    /// 提交方角色
    /// </summary>
    public AgentRole? SubmittedBy { get; init; }
    /// <summary>
    /// 来源描述
    /// </summary>
    public string? Source { get; init; }
    /// <summary>
    /// 来源链接
    /// </summary>
    public string? SourceUrl { get; init; }
    /// <summary>
    /// 权重（0.1-10.0）
    /// </summary>
    public double Weight { get; init; } = 1.0;
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>
    /// 验证时间
    /// </summary>
    public DateTime? VerifiedAt { get; set; }
    /// <summary>
    /// 验证方标识
    /// </summary>
    public string? VerifiedBy { get; set; }
}

/// <summary>
/// 推理节点类型
/// </summary>
public enum ReasoningNodeType
{
    /// <summary>
    /// 假定
    /// </summary>
    [EnumValue("assumption")] Assumption,
    /// <summary>
    /// 证据
    /// </summary>
    [EnumValue("evidence")] Evidence,
    /// <summary>
    /// 裁决
    /// </summary>
    [EnumValue("verdict")] Verdict,
}
