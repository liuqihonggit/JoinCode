namespace JoinCode.CodeIndex;

/// <summary>
/// 项目索引器 — 基于 InMemoryIndexStore 不可变快照 + CAS 无锁写入
/// 不再使用 SQLite 事务,所有写操作通过 CAS 原子完成
/// </summary>
internal sealed class ProjectIndex {
    private readonly InMemoryIndexStore _store;
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造项目索引器
    /// </summary>
    public ProjectIndex(InMemoryIndexStore store, IFileSystem fs, ILogger? logger = null) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fs);
        _store = store;
        _fs = fs;
        _logger = logger;
    }

    /// <summary>
    /// 索引单个 csproj 项目
    /// </summary>
    internal async Task IndexProjectAsync(string csprojPath, string workspaceRoot, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(csprojPath);
        if (!_fs.FileExists(csprojPath)) return;

        var parseResult = await CsprojParser.ParseAsync(csprojPath, _fs, workspaceRoot).ConfigureAwait(false);
        var (project, projectRefs, nugetRefs) = BuildProjectData(parseResult, projectGuid: null);
        _store.Update(snap => snap.IndexProject(parseResult.FilePath, project, projectRefs, nugetRefs));
    }

    /// <summary>
    /// 索引解决方案文件（.sln 或 .slnx）
    /// </summary>
    internal async Task IndexSolutionAsync(string solutionPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(solutionPath);
        if (!_fs.FileExists(solutionPath)) return;

        var workspaceRoot = Path.GetDirectoryName(solutionPath) ?? string.Empty;
        var parseResult = solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? await SolutionParser.ParseSlnxAsync(solutionPath, _fs).ConfigureAwait(false)
            : await SolutionParser.ParseSlnAsync(solutionPath, _fs).ConfigureAwait(false);

        foreach (var entry in parseResult.Projects) {
            if (_fs.FileExists(entry.RelativePath)) {
                try {
                    await IndexProjectWithGuidAsync(entry.RelativePath, workspaceRoot, entry.ProjectGuid, ct).ConfigureAwait(false);
                } catch (Exception ex) {
                    _logger?.LogWarning(ex, "ProjectIndex: 解析项目失败,跳过: {File}", entry.RelativePath);
                }
            }
        }
    }

    /// <summary>
    /// 移除指定项目的索引
    /// </summary>
    internal Task RemoveProjectAsync(string csprojPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(csprojPath);
        _store.Update(snap => snap.RemoveProject(csprojPath));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 清空所有项目索引数据
    /// </summary>
    internal Task ClearAsync(CancellationToken ct) {
        _store.Update(snap => snap.ClearProjects());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取已索引项目数量 — 无锁快照读取
    /// </summary>
    internal Task<int> GetProjectCountAsync(CancellationToken ct) {
        var snap = _store.GetSnapshot();
        return Task.FromResult(snap.Projects.Count);
    }

    private async Task IndexProjectWithGuidAsync(string csprojPath, string workspaceRoot, string projectGuid, CancellationToken ct) {
        if (!_fs.FileExists(csprojPath)) return;

        var parseResult = await CsprojParser.ParseAsync(csprojPath, _fs, workspaceRoot).ConfigureAwait(false);
        var (project, projectRefs, nugetRefs) = BuildProjectData(parseResult, projectGuid);
        _store.Update(snap => snap.IndexProject(parseResult.FilePath, project, projectRefs, nugetRefs));
    }

    private static (ProjectInfo, IReadOnlyList<ProjectReferenceEdge>, IReadOnlyList<NuGetPackageReference>) BuildProjectData(
        CsprojParseResult parseResult, string? projectGuid) {
        var project = new ProjectInfo {
            Name = parseResult.Name,
            FilePath = parseResult.FilePath,
            TargetFramework = parseResult.TargetFramework,
            OutputType = parseResult.OutputType,
            ProjectGuid = string.IsNullOrEmpty(projectGuid) ? null : projectGuid
        };

        var projectRefs = parseResult.ProjectReferences
            .Select(target => new ProjectReferenceEdge {
                SourceProjectPath = parseResult.FilePath,
                TargetProjectPath = target
            })
            .ToList();

        var nugetRefs = parseResult.PackageReferences
            .Select(pkg => new NuGetPackageReference {
                ProjectPath = parseResult.FilePath,
                PackageName = pkg.Name,
                Version = pkg.Version
            })
            .ToList();

        return (project, projectRefs, nugetRefs);
    }
}
