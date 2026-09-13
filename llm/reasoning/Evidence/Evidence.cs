namespace JoinCode.Reasoning.Evidence;

/// <summary>
/// 证据实体
/// </summary>
public sealed class EvidenceRecord
{
    /// <summary>
    /// 证据唯一标识
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 证据内容
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// 证据分类
    /// </summary>
    public required EvidenceCategory Category { get; init; }

    /// <summary>
    /// 信任级别
    /// </summary>
    public TrustLevel TrustLevel { get; set; } = TrustLevel.Moderate;

    /// <summary>
    /// 提交该证据的智能体角色
    /// </summary>
    public required AgentRole SubmittedBy { get; init; }

    /// <summary>
    /// 证据来源描述
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// 证据来源URL
    /// </summary>
    public string? SourceUrl { get; init; }

    /// <summary>
    /// 在来源中定位到的行号
    /// </summary>
    public int? LineNumber { get; set; }

    /// <summary>
    /// 从来源中提取的文本片段
    /// </summary>
    public string? ExtractedText { get; set; }

    /// <summary>
    /// URL是否已通过验证
    /// </summary>
    public bool IsUrlVerified { get; set; }

    /// <summary>
    /// URL验证时间
    /// </summary>
    public DateTime? UrlVerifiedAt { get; set; }

    /// <summary>
    /// 证据权重
    /// </summary>
    public double Weight { get; init; } = 1.0;

    /// <summary>
    /// 证据创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 支持该证据的声明标识列表
    /// </summary>
    public List<string> SupportingClaimIds { get; init; } = [];

    /// <summary>
    /// 反驳该证据的声明标识列表
    /// </summary>
    public List<string> RefutingClaimIds { get; init; } = [];
}
