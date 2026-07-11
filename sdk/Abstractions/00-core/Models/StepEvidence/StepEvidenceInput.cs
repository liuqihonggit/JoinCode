namespace JoinCode.Abstractions.Models.StepEvidence;

/// <summary>
/// 步骤完成证据输入项
/// </summary>
public sealed record StepEvidenceInput(
    [StringLength(20, ErrorMessage = "kind 过长")]
    string Kind = "",
    [StringLength(2000, ErrorMessage = "summary 过长")]
    string Summary = "",
    [StringLength(500, ErrorMessage = "command 过长")]
    string? Command = null,
    List<string>? Paths = null);
