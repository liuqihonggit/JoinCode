namespace JoinCode.Abstractions.CodeIndex;

public interface IDependencyGraph {
    /// <summary>异步获取指定符号的继承者列表。</summary>
    Task<IReadOnlyList<DependencyEdge>> GetInheritorsAsync(string symbolName, CancellationToken ct);
    /// <summary>异步获取指定符号的依赖列表。</summary>
    Task<IReadOnlyList<DependencyEdge>> GetDependenciesAsync(string symbolName, CancellationToken ct);
    /// <summary>异步获取受指定文件影响的文件列表。</summary>
    Task<IReadOnlyList<string>> GetAffectedFilesAsync(string filePath, CancellationToken ct);
}