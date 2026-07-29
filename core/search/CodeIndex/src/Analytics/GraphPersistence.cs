namespace JoinCode.CodeIndex.Analytics;

/// <summary>
/// 图持久化实现 — 将 InMemoryIndexStore 序列化为 JSON 文件
/// </summary>
[Register]
public sealed class GraphPersistence : IGraphPersistence
{
    private readonly InMemoryIndexStore _store;
    private readonly IFileSystem _fs;
    private const int CurrentVersion = 1;

    public GraphPersistence(InMemoryIndexStore store, IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fs);
        _store = store;
        _fs = fs;
    }

    public async Task SaveAsync(string directory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(directory);
        _fs.CreateDirectory(directory);

        using var scope = _store.EnterReadLock();

        var data = new GraphPersistenceData
        {
            Version = CurrentVersion,
            SavedAt = DateTimeOffset.UtcNow,
            Symbols = _store.SymbolsByFqn.Values.ToList(),
            CallEdges = _store.CallEdges,
            DependencyEdges = _store.DepEdges,
            Projects = _store.Projects.Values.ToList(),
            ProjectReferences = _store.ProjectRefs,
            NuGetReferences = _store.NuGetRefs,
        };

        var json = JsonSerializer.Serialize(data, CodeIndexJsonContext.Default.GraphPersistenceData);
        var path = Path.Combine(directory, "code-index.json");
        await _fs.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    public async Task<bool> LoadAsync(string directory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var path = Path.Combine(directory, "code-index.json");

        if (!_fs.FileExists(path))
            return false;

        var json = await _fs.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var data = JsonSerializer.Deserialize(json, CodeIndexJsonContext.Default.GraphPersistenceData);

        if (data is null || data.Version != CurrentVersion)
            return false;

        using var scope = _store.EnterWriteLock();
        _store.Clear();

        foreach (var sym in data.Symbols)
        {
            _store.SymbolsByFqn[sym.FullyQualifiedName] = sym;
            if (!_store.SymbolsByName.TryGetValue(sym.Name, out var nameList))
            {
                nameList = new List<SymbolInfo>();
                _store.SymbolsByName[sym.Name] = nameList;
            }
            nameList.Add(sym);

            if (!_store.SymbolsByFile.TryGetValue(sym.FilePath, out var fileList))
            {
                fileList = new List<SymbolInfo>();
                _store.SymbolsByFile[sym.FilePath] = fileList;
            }
            fileList.Add(sym);

            if (!_store.SymbolsByKind.TryGetValue(sym.Kind, out var kindList))
            {
                kindList = new List<SymbolInfo>();
                _store.SymbolsByKind[sym.Kind] = kindList;
            }
            kindList.Add(sym);
        }

        foreach (var edge in data.CallEdges)
        {
            _store.CallEdges.Add(edge);
            if (!_store.CallsByCaller.TryGetValue(edge.CallerSymbol, out var list))
            {
                list = new List<CallEdge>();
                _store.CallsByCaller[edge.CallerSymbol] = list;
            }
            list.Add(edge);

            if (!_store.CallsByCallee.TryGetValue(edge.CalleeSymbol, out var calleeList))
            {
                calleeList = new List<CallEdge>();
                _store.CallsByCallee[edge.CalleeSymbol] = calleeList;
            }
            calleeList.Add(edge);

            if (!_store.CallsByFile.TryGetValue(edge.CallSiteFilePath, out var fileList))
            {
                fileList = new List<CallEdge>();
                _store.CallsByFile[edge.CallSiteFilePath] = fileList;
            }
            fileList.Add(edge);
        }

        foreach (var edge in data.DependencyEdges)
        {
            _store.DepEdges.Add(edge);
            if (!_store.DepsBySource.TryGetValue(edge.SourceSymbol, out var list))
            {
                list = new List<DependencyEdge>();
                _store.DepsBySource[edge.SourceSymbol] = list;
            }
            list.Add(edge);

            if (!_store.DepsByTarget.TryGetValue(edge.TargetSymbol, out var targetList))
            {
                targetList = new List<DependencyEdge>();
                _store.DepsByTarget[edge.TargetSymbol] = targetList;
            }
            targetList.Add(edge);

            if (!string.IsNullOrEmpty(edge.SourceFilePath))
            {
                if (!_store.DepsByFile.TryGetValue(edge.SourceFilePath!, out var fileList))
                {
                    fileList = new List<DependencyEdge>();
                    _store.DepsByFile[edge.SourceFilePath!] = fileList;
                }
                fileList.Add(edge);
            }
        }

        foreach (var proj in data.Projects)
            _store.Projects[proj.FilePath] = proj;

        foreach (var ref_ in data.ProjectReferences)
            _store.ProjectRefs.Add(ref_);

        foreach (var pkg in data.NuGetReferences)
            _store.NuGetRefs.Add(pkg);

        _store.LastUpdated = data.SavedAt;
        return true;
    }

    public Task<bool> ExistsAsync(string directory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var path = Path.Combine(directory, "code-index.json");
        return Task.FromResult(_fs.FileExists(path));
    }
}
