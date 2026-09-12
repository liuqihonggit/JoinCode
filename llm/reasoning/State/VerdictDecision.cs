namespace JoinCode.Reasoning.State;

/// <summary>
/// 裁决决定
/// </summary>
public enum VerdictDecision
{
    [EnumValue("accept")] Accept,
    [EnumValue("reject")] Reject,
    [EnumValue("pending")] Pending,
    [EnumValue("partially_accept")] PartiallyAccept,
}
