namespace Structura.Dag;

/// <summary>
/// DAG 操作结果
/// </summary>
public sealed class DagResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string>? CyclePath { get; init; }

    public static DagResult Ok() => new() { Success = true };
    public static DagResult Fail(string message) => new() { Success = false, ErrorMessage = message };
    public static DagResult Cycle(IReadOnlyList<string> path) => new() { Success = false, ErrorMessage = "Cycle detected", CyclePath = path };
}
