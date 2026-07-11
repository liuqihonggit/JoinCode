namespace CodeIndex;

/// <summary>
/// 项目索引器 — 重写为基于 InMemoryIndexStore 的内存字典操作
/// 不再使用 SQLite 事务,所有写操作在写锁内原子完成
/// </summary>
internal sealed class ProjectIndex
{
    private readonly InMemoryIndexStore _store;
    private readonly IFileSystem _fs;

    public ProjectIndex(InMemoryIndexStore store, IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fs);
        _store = store;
        _fs = fs;
    }

    internal async Task IndexProjectAsync(string csprojPath, string workspaceRoot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(csprojPath);

        if (!_fs.FileExists(csprojPath))
        {
            return;
        }

        var parseResult = CsprojParser.Parse(csprojPath, _fs, workspaceRoot);
        await Task.CompletedTask.ConfigureAwait(false);

        using var scope = _store.EnterWriteLock();
        RemoveProjectInternal(csprojPath);
        InsertProjectInternal(parseResult);
        InsertProjectReferencesInternal(parseResult);
        InsertNuGetReferencesInternal(parseResult);
    }

    internal async Task IndexSolutionAsync(string solutionPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solutionPath);

        if (!_fs.FileExists(solutionPath))
        {
            return;
        }

        var workspaceRoot = Path.GetDirectoryName(solutionPath) ?? string.Empty;
        var parseResult = solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? SolutionParser.ParseSlnx(solutionPath)
            : SolutionParser.ParseSln(solutionPath, _fs);

        foreach (var entry in parseResult.Projects)
        {
            if (_fs.FileExists(entry.RelativePath))
            {
                await IndexProjectWithGuidAsync(entry.RelativePath, workspaceRoot, entry.ProjectGuid, ct).ConfigureAwait(false);
            }
        }
    }

    internal Task RemoveProjectAsync(string csprojPath, CancellationToken ct)
    {
        using var scope = _store.EnterWriteLock();
        RemoveProjectInternal(csprojPath);
        return Task.CompletedTask;
    }

    internal Task ClearAsync(CancellationToken ct)
    {
        using var scope = _store.EnterWriteLock();
        _store.Projects.Clear();
        _store.ProjectRefs.Clear();
        _store.NuGetRefs.Clear();
        return Task.CompletedTask;
    }

    internal Task<int> GetProjectCountAsync(CancellationToken ct)
    {
        using var scope = _store.EnterReadLock();
        return Task.FromResult(_store.Projects.Count);
    }

    private async Task IndexProjectWithGuidAsync(string csprojPath, string workspaceRoot, string projectGuid, CancellationToken ct)
    {
        if (!_fs.FileExists(csprojPath))
        {
            return;
        }

        var parseResult = CsprojParser.Parse(csprojPath, _fs, workspaceRoot);
        await Task.CompletedTask.ConfigureAwait(false);

        using var scope = _store.EnterWriteLock();
        RemoveProjectInternal(csprojPath);

        // 如有 GUID 则创建带 Guid 的 ProjectInfo(因 record init-only,需创建新实例)
        var projectInfo = new ProjectInfo
        {
            Name = parseResult.Name,
            FilePath = parseResult.FilePath,
            TargetFramework = parseResult.TargetFramework,
            OutputType = parseResult.OutputType,
            ProjectGuid = string.IsNullOrEmpty(projectGuid) ? null : projectGuid
        };

        _store.Projects[parseResult.FilePath] = projectInfo;
        InsertProjectReferencesInternal(parseResult);
        InsertNuGetReferencesInternal(parseResult);
    }

    private void RemoveProjectInternal(string csprojPath)
    {
        _store.Projects.Remove(csprojPath);

        // 移除该项目的所有 ProjectReference
        _store.ProjectRefs.RemoveAll(e => e.SourceProjectPath == csprojPath);

        // 移除该项目的所有 NuGet 引用
        _store.NuGetRefs.RemoveAll(p => p.ProjectPath == csprojPath);
    }

    private void InsertProjectInternal(CsprojParseResult parseResult)
    {
        _store.Projects[parseResult.FilePath] = new ProjectInfo
        {
            Name = parseResult.Name,
            FilePath = parseResult.FilePath,
            TargetFramework = parseResult.TargetFramework,
            OutputType = parseResult.OutputType,
            ProjectGuid = null
        };
    }

    private void InsertProjectReferencesInternal(CsprojParseResult parseResult)
    {
        foreach (var target in parseResult.ProjectReferences)
        {
            _store.ProjectRefs.Add(new ProjectReferenceEdge
            {
                SourceProjectPath = parseResult.FilePath,
                TargetProjectPath = target
            });
        }
    }

    private void InsertNuGetReferencesInternal(CsprojParseResult parseResult)
    {
        foreach (var pkg in parseResult.PackageReferences)
        {
            _store.NuGetRefs.Add(new NuGetPackageReference
            {
                ProjectPath = parseResult.FilePath,
                PackageName = pkg.Name,
                Version = pkg.Version
            });
        }
    }
}
