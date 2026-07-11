namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 步骤完成证据类型枚举
/// </summary>
public enum StepEvidenceKind
{
    [EnumValue("verification")] Verification,
    [EnumValue("diff")] Diff,
    [EnumValue("files")] Files,
    [EnumValue("manual")] Manual,
}
