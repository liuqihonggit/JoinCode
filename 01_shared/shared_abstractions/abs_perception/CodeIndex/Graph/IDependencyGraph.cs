namespace JoinCode.Abstractions.CodeIndex;

public interface IDependencyGraph
{
    Task<IReadOnlyList<DependencyEdge>> GetInheritorsAsync(string symbolName, CancellationToken ct);
    Task<IReadOnlyList<DependencyEdge>> GetDependenciesAsync(string symbolName, CancellationToken ct);
    Task<IReadOnlyList<string>> GetAffectedFilesAsync(string filePath, CancellationToken ct);
}
