namespace JoinCode.Reasoning.Evidence;

/// <summary>
/// 裁决结果
/// </summary>
public sealed class Verdict
{
    /// <summary>
    /// 裁决所针对的声明标识，构造时必须提供
    /// </summary>
    public required string ClaimId { get; init; }

    /// <summary>
    /// 裁决决定（如成立、不成立、待定），构造时必须提供
    /// </summary>
    public required VerdictDecision Decision { get; init; }

    /// <summary>
    /// 裁决理由，可选
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// 裁决置信度（0-100）
    /// </summary>
    public int Confidence { get; init; }
}
