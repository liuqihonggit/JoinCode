namespace JoinCode.CodeIndex;

/// <summary>
/// 项目依赖图 — 重写为基于 InMemoryIndexStore 的实时查询
/// store 已维护 Projects/ProjectRefs/NuGetRefs 数据,无需额外缓存层
/// </summary>
public sealed class ProjectDependencyGraph : IProjectDependencyGraph {
    private readonly InMemoryIndexStore _store;
    private int _cacheVersion;

    /// <summary>
    /// 构造项目依赖图
    /// </summary>
    /// <param name="store">内存索引存储</param>
    public ProjectDependencyGraph(InMemoryIndexStore store) {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// 使缓存失效 — 递增缓存版本号强制下次查询重新读取
    /// </summary>
    internal void InvalidateCache() {
        Interlocked.Increment(ref _cacheVersion);
    }

    /// <summary>
    /// 获取指定项目的直接依赖项
    /// </summary>
    /// <param name="projectPath">项目路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>项目引用边列表</returns>
    public Task<IReadOnlyList<ProjectReferenceEdge>> GetProjectDependenciesAsync(string projectPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(projectPath);

        var normalized = ResolveProjectPath(projectPath);
        using var scope = _store.EnterReadLock();
        var result = _store.ProjectRefs.TryGetValue(normalized, out var list)
            ? list.ToList()
            : new List<ProjectReferenceEdge>();
        return Task.FromResult<IReadOnlyList<ProjectReferenceEdge>>(result);
    }

    /// <summary>
    /// 获取直接依赖于指定项目的项目列表（反向依赖）
    /// </summary>
    /// <param name="projectPath">项目路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>项目引用边列表</returns>
    public Task<IReadOnlyList<ProjectReferenceEdge>> GetProjectDependentsAsync(string projectPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(projectPath);

        var normalized = ResolveProjectPath(projectPath);
        using var scope = _store.EnterReadLock();
        var result = _store.ProjectRefs.Values
            .SelectMany(v => v)
            .Where(e => NormalizePath(e.TargetProjectPath) == normalized)
            .ToList();
        return Task.FromResult<IReadOnlyList<ProjectReferenceEdge>>(result);
    }

    /// <summary>
    /// 获取受指定文件变更影响的所有项目 — 反向 BFS 查找所有直接或间接依赖该文件所属项目的项目
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>受影响的项目路径列表</returns>
    public async Task<IReadOnlyList<string>> GetAffectedProjectsAsync(string filePath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(filePath);

        var normalized = NormalizePath(filePath);
        var owningProject = await FindOwningProjectAsync(normalized, ct).ConfigureAwait(false);

        if (owningProject is null) {
            return Array.Empty<string>();
        }

        using var scope = _store.EnterReadLock();

        // 反向 BFS 查找所有依赖该项目(直接或间接)的项目
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        visited.Add(owningProject);
        queue.Enqueue(owningProject);

        while (queue.Count > 0) {
            var current = queue.Dequeue();
            var dependents = _store.ProjectRefs.Values
                .SelectMany(v => v)
                .Where(e => NormalizePath(e.TargetProjectPath) == current)
                .Select(e => e.SourceProjectPath);

            foreach (var dep in dependents) {
                if (visited.Add(dep)) {
                    queue.Enqueue(dep);
                }
            }
        }

        visited.Remove(owningProject);
        return visited.ToList();
    }

    /// <summary>
    /// 获取指定项目引用的 NuGet 包列表
    /// </summary>
    /// <param name="projectPath">项目路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>NuGet 包引用列表</returns>
    public Task<IReadOnlyList<NuGetPackageReference>> GetProjectNuGetPackagesAsync(string projectPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(projectPath);

        var normalized = ResolveProjectPath(projectPath);
        using var scope = _store.EnterReadLock();
        var result = _store.NuGetRefs.TryGetValue(normalized, out var list)
            ? list.ToList()
            : new List<NuGetPackageReference>();
        return Task.FromResult<IReadOnlyList<NuGetPackageReference>>(result);
    }

    /// <summary>
    /// 获取引用了指定 NuGet 包的所有项目
    /// </summary>
    /// <param name="packageName">NuGet 包名</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>项目路径列表</returns>
    public Task<IReadOnlyList<string>> GetProjectsUsingNuGetPackageAsync(string packageName, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(packageName);

        using var scope = _store.EnterReadLock();
        var result = _store.NuGetRefs.Values
            .SelectMany(v => v)
            .Where(p => string.Equals(p.PackageName, packageName, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.ProjectPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    /// <summary>
    /// 获取所有已索引的项目
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>项目信息列表</returns>
    public Task<IReadOnlyList<ProjectInfo>> GetAllProjectsAsync(CancellationToken ct) {
        using var scope = _store.EnterReadLock();
        return Task.FromResult<IReadOnlyList<ProjectInfo>>(_store.Projects.Values.ToList());
    }

    /// <summary>
    /// 查找包含指定文件的项目 — 选择路径最长（最深层）的匹配项目
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>所属项目路径；null 表示未找到</returns>
    internal Task<string?> FindOwningProjectAsync(string filePath, CancellationToken ct) {
        var normalizedFilePath = NormalizePath(filePath);

        using var scope = _store.EnterReadLock();
        string? bestMatch = null;
        var bestLength = 0;

        foreach (var project in _store.Projects.Values) {
            var projectDir = Path.GetDirectoryName(project.FilePath);
            bool isMatch;

            if (string.IsNullOrEmpty(projectDir)) {
                isMatch = string.Equals(normalizedFilePath, NormalizePath(project.FilePath), StringComparison.OrdinalIgnoreCase);
            } else {
                isMatch = normalizedFilePath.StartsWith(NormalizePath(projectDir), StringComparison.OrdinalIgnoreCase);
            }

            if (isMatch && project.FilePath.Length > bestLength) {
                bestMatch = project.FilePath;
                bestLength = project.FilePath.Length;
            }
        }

        return Task.FromResult(bestMatch);
    }

    private static string NormalizePath(string path) {
        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// 解析项目路径: 如果是相对路径,在 store 中查找以该路径结尾的绝对路径; 否则规范化
    /// </summary>
    private string ResolveProjectPath(string projectPath) {
        var normalized = NormalizePath(projectPath);

        // 绝对路径直接返回
        if (Path.IsPathRooted(normalized)) {
            return normalized;
        }

        // 相对路径: 在 store 中查找以该路径结尾的项目
        using var scope = _store.EnterReadLock();
        foreach (var key in _store.Projects.Keys) {
            var normalizedKey = NormalizePath(key);
            if (normalizedKey.EndsWith(normalized, StringComparison.OrdinalIgnoreCase)) {
                return normalizedKey;
            }
        }

        return normalized;
    }
}