namespace JoinCode.Abstractions.LLM.Chat;

public sealed class PersistedToolResult
{
    public required string Filepath { get; init; }
    public required int OriginalSize { get; init; }
    public required bool IsJson { get; init; }
    public required string Preview { get; init; }
    public required bool HasMore { get; init; }
}
