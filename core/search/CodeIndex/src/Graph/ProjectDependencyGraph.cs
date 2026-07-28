namespace JoinCode.CodeIndex;

/// <summary>
/// 项目依赖图 — 重写为基于 InMemoryIndexStore 的实时查询
/// store 已维护 Projects/ProjectRefs/NuGetRefs 数据,无需额外缓存层
/// </summary>
public sealed class ProjectDependencyGraph : IProjectDependencyGraph
{
    private readonly InMemoryIndexStore _store;
    private int _cacheVersion;

    public ProjectDependencyGraph(InMemoryIndexStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    internal void InvalidateCache()
    {
        Interlocked.Increment(ref _cacheVersion);
    }

    public Task<IReadOnlyList<ProjectReferenceEdge>> GetProjectDependenciesAsync(string projectPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectPath);

        var normalized = NormalizePath(projectPath);
        using var scope = _store.EnterReadLock();
        var result = _store.ProjectRefs
            .Where(e => NormalizePath(e.SourceProjectPath) == normalized)
            .ToList();
        return Task.FromResult<IReadOnlyList<ProjectReferenceEdge>>(result);
    }

    public Task<IReadOnlyList<ProjectReferenceEdge>> GetProjectDependentsAsync(string projectPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectPath);

        var normalized = NormalizePath(projectPath);
        using var scope = _store.EnterReadLock();
        var result = _store.ProjectRefs
            .Where(e => NormalizePath(e.TargetProjectPath) == normalized)
            .ToList();
        return Task.FromResult<IReadOnlyList<ProjectReferenceEdge>>(result);
    }

    public async Task<IReadOnlyList<string>> GetAffectedProjectsAsync(string filePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var normalized = NormalizePath(filePath);
        var owningProject = await FindOwningProjectAsync(normalized, ct).ConfigureAwait(false);

        if (owningProject is null)
        {
            return Array.Empty<string>();
        }

        using var scope = _store.EnterReadLock();

        // 反向 BFS 查找所有依赖该项目(直接或间接)的项目
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        visited.Add(owningProject);
        queue.Enqueue(owningProject);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var dependents = _store.ProjectRefs
                .Where(e => NormalizePath(e.TargetProjectPath) == current)
                .Select(e => e.SourceProjectPath);

            foreach (var dep in dependents)
            {
                if (visited.Add(dep))
                {
                    queue.Enqueue(dep);
                }
            }
        }

        visited.Remove(owningProject);
        return visited.ToList();
    }

    public Task<IReadOnlyList<NuGetPackageReference>> GetProjectNuGetPackagesAsync(string projectPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectPath);

        var normalized = NormalizePath(projectPath);
        using var scope = _store.EnterReadLock();
        var result = _store.NuGetRefs
            .Where(p => NormalizePath(p.ProjectPath) == normalized)
            .ToList();
        return Task.FromResult<IReadOnlyList<NuGetPackageReference>>(result);
    }

    public Task<IReadOnlyList<string>> GetProjectsUsingNuGetPackageAsync(string packageName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(packageName);

        using var scope = _store.EnterReadLock();
        var result = _store.NuGetRefs
            .Where(p => string.Equals(p.PackageName, packageName, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.ProjectPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    public Task<IReadOnlyList<ProjectInfo>> GetAllProjectsAsync(CancellationToken ct)
    {
        using var scope = _store.EnterReadLock();
        return Task.FromResult<IReadOnlyList<ProjectInfo>>(_store.Projects.Values.ToList());
    }

    internal Task<string?> FindOwningProjectAsync(string filePath, CancellationToken ct)
    {
        var normalizedFilePath = NormalizePath(filePath);

        using var scope = _store.EnterReadLock();
        string? bestMatch = null;
        var bestLength = 0;

        foreach (var project in _store.Projects.Values)
        {
            var projectDir = Path.GetDirectoryName(project.FilePath);
            bool isMatch;

            if (string.IsNullOrEmpty(projectDir))
            {
                isMatch = string.Equals(normalizedFilePath, NormalizePath(project.FilePath), StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                isMatch = normalizedFilePath.StartsWith(NormalizePath(projectDir), StringComparison.OrdinalIgnoreCase);
            }

            if (isMatch && project.FilePath.Length > bestLength)
            {
                bestMatch = project.FilePath;
                bestLength = project.FilePath.Length;
            }
        }

        return Task.FromResult(bestMatch);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }
}
