namespace JoinCode.Abstractions.CodeIndex;

public interface IProjectDependencyGraph
{
    Task<IReadOnlyList<ProjectReferenceEdge>> GetProjectDependenciesAsync(string projectPath, CancellationToken ct);
    Task<IReadOnlyList<ProjectReferenceEdge>> GetProjectDependentsAsync(string projectPath, CancellationToken ct);
    Task<IReadOnlyList<string>> GetAffectedProjectsAsync(string filePath, CancellationToken ct);
    Task<IReadOnlyList<NuGetPackageReference>> GetProjectNuGetPackagesAsync(string projectPath, CancellationToken ct);
    Task<IReadOnlyList<string>> GetProjectsUsingNuGetPackageAsync(string packageName, CancellationToken ct);
    Task<IReadOnlyList<ProjectInfo>> GetAllProjectsAsync(CancellationToken ct);
}
