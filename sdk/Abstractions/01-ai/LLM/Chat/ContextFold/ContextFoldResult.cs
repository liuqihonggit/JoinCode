namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ContextFoldResult
{
    public bool Folded { get; init; }
    public int HeadMessageCount { get; init; }
    public int TailMessageCount { get; init; }
    public int OriginalMessageCount { get; init; }
    public string Summary { get; init; } = string.Empty;
    public ContextFoldDecision Decision { get; init; }
}
