namespace JoinCode.Reasoning.State;

/// <summary>
/// 数据状态枚举 — 假定→验证→事实 的三态跃迁
/// </summary>
public enum DataState
{
    /// <summary>假定 — 待验证的命题</summary>
    [EnumValue("assumption")] Assumption,
    /// <summary>已验证 — 通过验证但未提升为事实</summary>
    [EnumValue("verified")] Verified,
    /// <summary>事实 — 已确认成立的命题</summary>
    [EnumValue("fact")] Fact,
    /// <summary>已驳回 — 验证失败被否决</summary>
    [EnumValue("rejected")] Rejected,
    /// <summary>待举证 — 等待补充证据</summary>
    [EnumValue("pending_evidence")] PendingEvidence,
}
