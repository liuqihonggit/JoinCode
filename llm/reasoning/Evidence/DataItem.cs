namespace JoinCode.Reasoning.Evidence;

/// <summary>
/// 数据项 — 推理链中的基本单元
/// </summary>
public sealed class DataItem
{
    /// <summary>
    /// 唯一标识符，默认生成新的 GUID（N 格式无连字符）
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 数据项内容，构造时必须提供
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// 数据状态，默认为假定（Assumption）
    /// </summary>
    public DataState State { get; set; } = DataState.Assumption;

    /// <summary>
    /// 来源描述，可选
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// 置信度（0-100），默认 50
    /// </summary>
    public int Confidence { get; set; } = 50;

    /// <summary>
    /// 创建时间（UTC），默认当前时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 验证时间（UTC），未验证时为 null
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>
    /// 验证者标识，未验证时为 null
    /// </summary>
    public string? VerifiedBy { get; set; }

    /// <summary>
    /// 支持该数据项的证据标识列表
    /// </summary>
    public List<string> EvidenceIds { get; set; } = [];

    /// <summary>
    /// 反驳该数据项的反证标识列表
    /// </summary>
    public List<string> CounterIds { get; set; } = [];

    /// <summary>
    /// 提交该数据项的 Agent 角色，可选
    /// </summary>
    public AgentRole? SubmittedBy { get; init; }
}
