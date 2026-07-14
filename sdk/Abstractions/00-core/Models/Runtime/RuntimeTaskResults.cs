namespace JoinCode.Abstractions.Models.Runtime;

public sealed record RuntimeTaskListResult
{
    public bool Success { get; init; }
    public IReadOnlyList<RuntimeTask> Tasks { get; init; } = Array.Empty<RuntimeTask>();
    public int TotalCount { get; init; }
    public string? ErrorMessage { get; init; }

    public static RuntimeTaskListResult Ok(IReadOnlyList<RuntimeTask> tasks, int totalCount) =>
        new() { Success = true, Tasks = tasks, TotalCount = totalCount };
    public static RuntimeTaskListResult Fail(string error) =>
        new() { Success = false, ErrorMessage = error };
}
