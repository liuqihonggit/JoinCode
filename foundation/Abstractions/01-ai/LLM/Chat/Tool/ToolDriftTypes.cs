namespace JoinCode.Abstractions.LLM.Chat;

public enum ToolDriftKind
{
    Identity,
    Append,
    Edit,
    Reorder,
    Remove
}

public sealed class ToolDriftReport
{
    public ToolDriftKind Kind { get; init; }
    public IReadOnlyList<string> AddedNames { get; init; } = [];
    public IReadOnlyList<string> RemovedNames { get; init; } = [];
    public IReadOnlyList<string> EditedNames { get; init; } = [];
    public IReadOnlyList<string> ReorderedNames { get; init; } = [];
    public string Summary { get; init; } = string.Empty;

    public bool IsCacheSafe => Kind is ToolDriftKind.Identity or ToolDriftKind.Append;
}
