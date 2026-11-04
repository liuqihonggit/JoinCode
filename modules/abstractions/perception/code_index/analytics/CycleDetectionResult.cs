namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 环检测结果 — 包含调用图环和依赖图环
/// </summary>
public sealed record CycleDetectionResult {
    /// <summary>获取调用环列表。</summary>
    public required IReadOnlyList<IReadOnlyList<string>> CallCycles { get; init; }
    /// <summary>获取依赖环列表。</summary>
    public required IReadOnlyList<IReadOnlyList<string>> DependencyCycles { get; init; }
    /// <summary>获取是否存在调用环。</summary>
    public required bool HasCallCycles { get; init; }
    /// <summary>获取是否存在依赖环。</summary>
    public required bool HasDependencyCycles { get; init; }
}
