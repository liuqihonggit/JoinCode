namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ContextFoldThresholds
{
    public double FoldThreshold { get; init; } = 0.5;
    public double AggressiveThreshold { get; init; } = 0.7;
    public double ForceSummaryThreshold { get; init; } = 0.8;
    public double EmergencyThreshold { get; init; } = 0.95;
    public double TailFraction { get; init; } = 0.2;
    public double AggressiveTailFraction { get; init; } = 0.1;
    public double MinSavingsFraction { get; init; } = 0.3;
    public int CharsPerToken { get; init; } = 4;

    public static ContextFoldThresholds Default { get; } = new();
}
