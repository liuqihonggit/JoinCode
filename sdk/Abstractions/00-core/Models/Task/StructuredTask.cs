namespace JoinCode.Abstractions.Models.Task;

public sealed record TaskPossibility
{
    public required string Description { get; init; }
    public bool Excluded { get; init; }
    public string? ExclusionReason { get; init; }
}

public sealed record StructuredTaskEntry
{
    public required int Order { get; init; }
    public required string Description { get; init; }
    public string? Result { get; init; }
    public List<TaskPossibility> Possibilities { get; init; } = new();
    public string Status { get; init; } = TaskStatusConstants.Pending;
}
