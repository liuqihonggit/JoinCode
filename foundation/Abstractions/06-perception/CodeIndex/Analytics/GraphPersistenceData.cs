namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 图持久化数据结构 — 用于 JSON 序列化/反序列化 InMemoryIndexStore
/// </summary>
public sealed record GraphPersistenceData
{
    public required int Version { get; init; }
    public required DateTimeOffset SavedAt { get; init; }
    public required List<SymbolInfo> Symbols { get; init; }
    public required List<CallEdge> CallEdges { get; init; }
    public required List<DependencyEdge> DependencyEdges { get; init; }
    public required List<ProjectInfo> Projects { get; init; }
    public required List<ProjectReferenceEdge> ProjectReferences { get; init; }
    public required List<NuGetPackageReference> NuGetReferences { get; init; }
}
