namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 环检测结果 — 包含调用图环和依赖图环
/// </summary>
public sealed record CycleDetectionResult
{
    public required IReadOnlyList<IReadOnlyList<string>> CallCycles { get; init; }
    public required IReadOnlyList<IReadOnlyList<string>> DependencyCycles { get; init; }
    public required bool HasCallCycles { get; init; }
    public required bool HasDependencyCycles { get; init; }
}
