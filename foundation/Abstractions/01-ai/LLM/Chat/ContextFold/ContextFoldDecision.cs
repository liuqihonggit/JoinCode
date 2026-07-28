namespace JoinCode.Abstractions.LLM.Chat;

public enum ContextFoldDecision
{
    None,
    FoldNormal,
    FoldAggressive,
    ExitWithSummary
}
