namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 图持久化数据结构 — 用于 JSON 序列化/反序列化 InMemoryIndexStore
/// </summary>
public sealed record GraphPersistenceData {
    /// <summary>获取数据版本号。</summary>
    public required int Version { get; init; }
    /// <summary>获取保存时间。</summary>
    public required DateTimeOffset SavedAt { get; init; }
    /// <summary>获取符号列表。</summary>
    public required List<SymbolInfo> Symbols { get; init; }
    /// <summary>获取调用边列表。</summary>
    public required List<CallEdge> CallEdges { get; init; }
    /// <summary>获取依赖边列表。</summary>
    public required List<DependencyEdge> DependencyEdges { get; init; }
    /// <summary>获取项目列表。</summary>
    public required List<ProjectInfo> Projects { get; init; }
    /// <summary>获取项目引用边列表。</summary>
    public required List<ProjectReferenceEdge> ProjectReferences { get; init; }
    /// <summary>获取 NuGet 包引用列表。</summary>
    public required List<NuGetPackageReference> NuGetReferences { get; init; }
    /// <summary>获取文件跟踪信息列表。</summary>
    public required List<FileTrackingInfo> FileTracking { get; init; }
}