namespace JoinCode.Reasoning.State;

/// <summary>
/// 裁决决定
/// </summary>
public enum VerdictDecision {
    /// <summary>接受 — 裁决采纳该命题</summary>
    [EnumValue("accept")] Accept,
    /// <summary>驳回 — 裁决否决该命题</summary>
    [EnumValue("reject")] Reject,
    /// <summary>待决 — 证据不足，暂缓裁决</summary>
    [EnumValue("pending")] Pending,
    /// <summary>部分接受 — 命题成立但附带条件</summary>
    [EnumValue("partially_accept")] PartiallyAccept,
}