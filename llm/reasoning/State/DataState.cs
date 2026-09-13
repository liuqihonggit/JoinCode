namespace JoinCode.Reasoning.State;

/// <summary>
/// 数据状态枚举 — 假定→验证→事实 的三态跃迁
/// </summary>
public enum DataState
{
    [EnumValue("assumption")] Assumption,
    [EnumValue("verified")] Verified,
    [EnumValue("fact")] Fact,
    [EnumValue("rejected")] Rejected,
    [EnumValue("pending_evidence")] PendingEvidence,
}
