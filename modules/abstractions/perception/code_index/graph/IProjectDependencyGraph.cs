namespace JoinCode.Abstractions.CodeIndex;

public interface IProjectDependencyGraph {
    /// <summary>异步获取项目的依赖列表。</summary>
    Task<IReadOnlyList<ProjectReferenceEdge>> GetProjectDependenciesAsync(string projectPath, CancellationToken ct);
    /// <summary>异步获取依赖该项目的项目列表。</summary>
    Task<IReadOnlyList<ProjectReferenceEdge>> GetProjectDependentsAsync(string projectPath, CancellationToken ct);
    /// <summary>异步获取受指定文件影响的项目列表。</summary>
    Task<IReadOnlyList<string>> GetAffectedProjectsAsync(string filePath, CancellationToken ct);
    /// <summary>异步获取项目引用的 NuGet 包列表。</summary>
    Task<IReadOnlyList<NuGetPackageReference>> GetProjectNuGetPackagesAsync(string projectPath, CancellationToken ct);
    /// <summary>异步获取使用指定 NuGet 包的项目列表。</summary>
    Task<IReadOnlyList<string>> GetProjectsUsingNuGetPackageAsync(string packageName, CancellationToken ct);
    /// <summary>异步获取所有项目列表。</summary>
    Task<IReadOnlyList<ProjectInfo>> GetAllProjectsAsync(CancellationToken ct);
}