namespace JoinCode.Abstractions.LLM.Chat;

public sealed class PreflightDecision
{
    public bool NeedsAction { get; init; }
    public double EstimatedRatio { get; init; }
}
