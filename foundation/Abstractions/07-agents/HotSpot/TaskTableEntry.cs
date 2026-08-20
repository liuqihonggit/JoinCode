namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 任务表条目 — 任务表.md 的一行，含热点标注
/// </summary>
public sealed record TaskTableEntry
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Files { get; init; } = [];
    public required string Role { get; init; } = "worker";
    public required IReadOnlyList<string> Dependencies { get; init; } = [];
    public required string Verification { get; init; } = string.Empty;
    public required bool IsHotFile { get; init; }
    public string HotSpotAnnotation { get; init; } = string.Empty;
    public required string Status { get; init; } = "pending";
}
