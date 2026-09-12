namespace JoinCode.Abstractions.LLM.Chat;

public enum ContextFoldDecision
{
    None,
    Deferred,
    FoldNormal,
    FoldAggressive,
    ExitWithSummary
}
