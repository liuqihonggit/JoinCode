namespace JoinCode.Reasoning.Cone;

/// <summary>
/// 观察链片段 — 零散但可结构化的认知单元，关联 DAG 中的源数据项
/// </summary>
public sealed class ObservationFragment
{
    /// <summary>
    /// 片段唯一标识
    /// </summary>
    public string FragmentId { get; init; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// 关联的 DAG 源数据项 ID（DataItem.Id 或 EvidenceRecord.Id）
    /// </summary>
    public required string SourceItemId { get; init; }

    /// <summary>
    /// 所属角色链
    /// </summary>
    public required AgentRole RoleChain { get; init; }

    /// <summary>
    /// 原始片段文本
    /// </summary>
    public string RawText { get; init; } = string.Empty;

    /// <summary>
    /// 认知指纹
    /// </summary>
    public CognitiveFingerprint Fingerprint { get; init; } = new();

    /// <summary>
    /// 折叠后向上层提交的摘要
    /// </summary>
    public string FoldedSummary { get; set; } = string.Empty;

    /// <summary>
    /// 展开条件，如"当法官质问物证链时"
    /// </summary>
    public string ExpandCondition { get; init; } = string.Empty;

    /// <summary>
    /// 多叉指向 — 同一片段在不同角色链中下一跳不同
    /// </summary>
    public List<string> NextFragmentIds { get; init; } = [];

    /// <summary>
    /// 反链 — 被谁引用，便于追溯观察链
    /// </summary>
    public List<string> BackReferences { get; init; } = [];

    /// <summary>
    /// 该角色载入此片段的顺序
    /// </summary>
    public int LoadOrder { get; set; }

    /// <summary>
    /// 当前是否在视锥中展开
    /// </summary>
    public bool IsExpanded { get; set; } = true;
}
