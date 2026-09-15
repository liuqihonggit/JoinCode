namespace JoinCode.Abstractions.LLM.Chat;

public enum ContextFoldDecision
{
    [EnumValue("none")]
    None,
    [EnumValue("deferred")]
    Deferred,
    [EnumValue("fold_normal")]
    FoldNormal,
    [EnumValue("fold_aggressive")]
    FoldAggressive,
    [EnumValue("exit_with_summary")]
    ExitWithSummary
}
