namespace JoinCode.CodeIndex;

/// <summary>
/// 项目索引器 — 重写为基于 InMemoryIndexStore 的内存字典操作
/// 不再使用 SQLite 事务,所有写操作在写锁内原子完成
/// </summary>
internal sealed class ProjectIndex
{
    private readonly InMemoryIndexStore _store;
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造项目索引器
    /// </summary>
    /// <param name="store">内存索引存储</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">日志记录器（可选）</param>
    public ProjectIndex(InMemoryIndexStore store, IFileSystem fs, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fs);
        _store = store;
        _fs = fs;
        _logger = logger;
    }

    /// <summary>
    /// 索引单个 csproj 项目
    /// </summary>
    /// <param name="csprojPath">csproj 文件路径</param>
    /// <param name="workspaceRoot">工作区根路径</param>
    /// <param name="ct">取消令牌</param>
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

    /// <summary>
    /// 索引解决方案文件（.sln 或 .slnx）
    /// </summary>
    /// <param name="solutionPath">解决方案文件路径</param>
    /// <param name="ct">取消令牌</param>
    internal async Task IndexSolutionAsync(string solutionPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solutionPath);

        if (!_fs.FileExists(solutionPath))
        {
            return;
        }

        var workspaceRoot = Path.GetDirectoryName(solutionPath) ?? string.Empty;
        var parseResult = solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? SolutionParser.ParseSlnx(solutionPath, _fs)
            : SolutionParser.ParseSln(solutionPath, _fs);

        foreach (var entry in parseResult.Projects)
        {
            if (_fs.FileExists(entry.RelativePath))
            {
                try
                {
                    await IndexProjectWithGuidAsync(entry.RelativePath, workspaceRoot, entry.ProjectGuid, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "ProjectIndex: 解析项目失败,跳过: {File}", entry.RelativePath);
                }
            }
        }
    }

    /// <summary>
    /// 移除指定项目的索引
    /// </summary>
    /// <param name="csprojPath">csproj 文件路径</param>
    /// <param name="ct">取消令牌</param>
    internal Task RemoveProjectAsync(string csprojPath, CancellationToken ct)
    {
        using var scope = _store.EnterWriteLock();
        RemoveProjectInternal(csprojPath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 清空所有索引数据
    /// </summary>
    /// <param name="ct">取消令牌</param>
    internal Task ClearAsync(CancellationToken ct)
    {
        using var scope = _store.EnterWriteLock();
        _store.Projects.Clear();
        _store.ProjectRefs.Clear();
        _store.NuGetRefs.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取已索引项目数量
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>项目数量</returns>
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
        _store.ProjectRefs.Remove(csprojPath);

        // 移除该项目的所有 NuGet 引用
        _store.NuGetRefs.Remove(csprojPath);
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
        if (parseResult.ProjectReferences.Count == 0) return;
        if (!_store.ProjectRefs.TryGetValue(parseResult.FilePath, out var refList))
        {
            refList = new List<ProjectReferenceEdge>();
            _store.ProjectRefs[parseResult.FilePath] = refList;
        }
        foreach (var target in parseResult.ProjectReferences)
        {
            refList.Add(new ProjectReferenceEdge
            {
                SourceProjectPath = parseResult.FilePath,
                TargetProjectPath = target
            });
        }
    }

    private void InsertNuGetReferencesInternal(CsprojParseResult parseResult)
    {
        if (parseResult.PackageReferences.Count == 0) return;
        if (!_store.NuGetRefs.TryGetValue(parseResult.FilePath, out var refList))
        {
            refList = new List<NuGetPackageReference>();
            _store.NuGetRefs[parseResult.FilePath] = refList;
        }
        foreach (var pkg in parseResult.PackageReferences)
        {
            refList.Add(new NuGetPackageReference
            {
                ProjectPath = parseResult.FilePath,
                PackageName = pkg.Name,
                Version = pkg.Version
            });
        }
    }
}
