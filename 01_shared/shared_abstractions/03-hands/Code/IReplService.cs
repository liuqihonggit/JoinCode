namespace JoinCode.Abstractions.Interfaces;

public sealed class ReplResult
{
    public required bool Success { get; init; }
    public required string Output { get; init; }
    public required string Language { get; init; }
    public required TimeSpan ExecutionTime { get; init; }
    public string? Error { get; init; }
}

public sealed class ReplLanguageInfo
{
    public required string Language { get; init; }
    public required string DisplayName { get; init; }
    public required string Executable { get; init; }
    public required bool IsAvailable { get; init; }
    public string? InstallHint { get; init; }
}

public interface IReplService
{
    bool IsReplModeEnabled { get; }
    void EnableReplMode();
    void DisableReplMode();
    Task<ReplResult> ExecuteAsync(string code, string language = "csharp", int timeoutSeconds = 30, CancellationToken ct = default);
    IReadOnlyList<string> GetHiddenTools();
    IReadOnlyList<ReplLanguageInfo> GetAvailableLanguages();
}
