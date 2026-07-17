namespace JoinCode.Abstractions.LLM.Chat;

public sealed class PromptStateSnapshot
{
    public required string SystemPromptHash { get; init; }
    public required string ToolSpecsHash { get; init; }
    public required int ToolCount { get; init; }
    public required string ToolNamesHash { get; init; }
    public required string DynamicContentHash { get; init; }
    public IReadOnlyList<ToolSpec> ToolSpecs { get; init; } = [];
    public string? ModelId { get; init; }
    public bool? FastMode { get; init; }
}

public enum CacheBreakKind
{
    None,
    SystemPromptChanged,
    ToolSpecsChanged,
    DynamicContentChanged,
    CacheEviction,
    ModelChanged,
    FastModeChanged
}

public sealed class CacheBreakResult
{
    public bool BreakDetected { get; init; }
    public CacheBreakKind Kind { get; init; }
    public string? Detail { get; init; }
    public ToolDriftReport? ToolDrift { get; init; }

    public static CacheBreakResult NoBreak() => new() { BreakDetected = false, Kind = CacheBreakKind.None };

    public static CacheBreakResult Break(CacheBreakKind kind, string detail, ToolDriftReport? toolDrift = null) => new()
    {
        BreakDetected = true,
        Kind = kind,
        Detail = detail,
        ToolDrift = toolDrift
    };
}
