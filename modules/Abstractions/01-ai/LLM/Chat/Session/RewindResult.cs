namespace JoinCode.Abstractions.LLM.Chat;

public sealed class RewindResult
{
    public bool Success { get; init; }
    public int RemovedCount { get; init; }
    public int RemainingCount { get; init; }
    public RewindKind Kind { get; init; }
    public string? ErrorMessage { get; init; }

    public static RewindResult Ok(RewindKind kind, int removed, int remaining) => new()
    {
        Success = true,
        Kind = kind,
        RemovedCount = removed,
        RemainingCount = remaining
    };

    public static RewindResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };
}

public enum RewindKind
{
    TrimLastTurn,
    TruncateToIndex,
    ClearHistory
}
