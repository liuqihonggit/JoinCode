using Structura.Dag;

namespace JoinCode.CodeIndex.Analytics;

/// <summary>
/// 图分析实现 — 基于 InMemoryIndexStore 的图数据 + Structura DAG 算法
/// 社区检测: 标签传播; 枢纽分析: 度排序; 死代码: 无调用方检测
/// 环检测/拓扑排序: 构建 Dag&lt;string&gt; 委托 Structura 算法
/// </summary>
[Register]
public sealed class GraphAnalytics : IGraphAnalytics
{
    private readonly InMemoryIndexStore _store;

    public GraphAnalytics(InMemoryIndexStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Task<IReadOnlyList<CommunityInfo>> DetectCommunitiesAsync(CancellationToken ct)
    {
        using var scope = _store.EnterReadLock();
        var labels = LabelPropagation(_store.CallsByCaller, _store.CallsByCallee);
        var communities = BuildCommunities(labels, _store.CallsByCaller, _store.CallsByCallee);
        return Task.FromResult<IReadOnlyList<CommunityInfo>>(communities);
    }

    public Task<IReadOnlyList<HubNodeInfo>> GetHubNodesAsync(int topN, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(topN < 1 ? null : nameof(topN));
        using var scope = _store.EnterReadLock();

        var degreeMap = new Dictionary<string, (int In, int Out)>(StringComparer.Ordinal);

        foreach (var kvp in _store.CallsByCallee)
        {
            var sym = kvp.Key;
            var current = degreeMap.GetValueOrDefault(sym);
            degreeMap[sym] = (current.In + kvp.Value.Count, current.Out);
        }

        foreach (var kvp in _store.CallsByCaller)
        {
            var sym = kvp.Key;
            var current = degreeMap.GetValueOrDefault(sym);
            degreeMap[sym] = (current.In, current.Out + kvp.Value.Count);
        }

        var hubs = degreeMap
            .Select(kvp => new HubNodeInfo
            {
                SymbolName = kvp.Key,
                InDegree = kvp.Value.In,
                OutDegree = kvp.Value.Out,
                TotalDegree = kvp.Value.In + kvp.Value.Out,
                FilePath = FindFilePath(kvp.Key),
            })
            .OrderByDescending(h => h.TotalDegree)
            .Take(topN)
            .ToList();

        return Task.FromResult<IReadOnlyList<HubNodeInfo>>(hubs);
    }

    public Task<IReadOnlyList<DeadCodeEntry>> DetectDeadCodeAsync(CancellationToken ct)
    {
        using var scope = _store.EnterReadLock();
        var dead = new List<DeadCodeEntry>();

        foreach (var kvp in _store.SymbolsByFqn)
        {
            var symbol = kvp.Value;
            if (symbol.Kind != SymbolKind.Method && symbol.Kind != SymbolKind.LocalFunction)
                continue;

            if (symbol.Accessibility == "public" || symbol.Accessibility == "internal")
                continue;

            if (IsEntryPoint(symbol))
                continue;

            if (!_store.CallsByCallee.ContainsKey(symbol.FullyQualifiedName) &&
                !_store.CallsByCallee.ContainsKey(symbol.Name))
            {
                dead.Add(new DeadCodeEntry
                {
                    SymbolName = symbol.FullyQualifiedName,
                    FilePath = symbol.FilePath,
                    Line = symbol.StartLine,
                    Reason = "No callers found in index",
                });
            }
        }

        return Task.FromResult<IReadOnlyList<DeadCodeEntry>>(dead);
    }

    public Task<SubgraphResult> ExtractSubgraphAsync(string centerSymbol, int hops, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(centerSymbol);
        using var scope = _store.EnterReadLock();

        var nodes = new HashSet<string>(StringComparer.Ordinal) { centerSymbol };
        var edges = new List<CallEdge>();
        var frontier = new HashSet<string>(StringComparer.Ordinal) { centerSymbol };

        for (int i = 0; i < hops && frontier.Count > 0; i++)
        {
            var nextFrontier = new HashSet<string>(StringComparer.Ordinal);

            foreach (var sym in frontier)
            {
                if (_store.CallsByCaller.TryGetValue(sym, out var callees))
                {
                    foreach (var edge in callees)
                    {
                        edges.Add(edge);
                        if (nodes.Add(edge.CalleeSymbol))
                            nextFrontier.Add(edge.CalleeSymbol);
                    }
                }

                if (_store.CallsByCallee.TryGetValue(sym, out var callers))
                {
                    foreach (var edge in callers)
                    {
                        edges.Add(edge);
                        if (nodes.Add(edge.CallerSymbol))
                            nextFrontier.Add(edge.CallerSymbol);
                    }
                }
            }

            frontier = nextFrontier;
        }

        return Task.FromResult(new SubgraphResult
        {
            CenterSymbol = centerSymbol,
            Hops = hops,
            Nodes = nodes.ToList(),
            Edges = edges,
        });
    }

    public Task<ChangeImpactResult> AnalyzeChangeImpactAsync(IReadOnlyList<string> changedFiles, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(changedFiles);
        using var scope = _store.EnterReadLock();

        var affectedSymbols = new HashSet<string>(StringComparer.Ordinal);
        var affectedFiles = new HashSet<string>(StringComparer.Ordinal, changedFiles);
        var queue = new Queue<string>();

        foreach (var file in changedFiles)
        {
            if (!_store.SymbolsByFile.TryGetValue(file, out var symbols)) continue;
            foreach (var sym in symbols)
            {
                affectedSymbols.Add(sym.FullyQualifiedName);
                queue.Enqueue(sym.FullyQualifiedName);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!_store.CallsByCallee.TryGetValue(current, out var callers)) continue;

            foreach (var edge in callers)
            {
                if (affectedSymbols.Add(edge.CallerSymbol))
                {
                    queue.Enqueue(edge.CallerSymbol);
                    if (!string.IsNullOrEmpty(edge.CallSiteFilePath))
                        affectedFiles.Add(edge.CallSiteFilePath);
                }
            }
        }

        var affectedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in affectedFiles)
        {
            foreach (var proj in _store.Projects.Values)
            {
                if (file.StartsWith(Path.GetDirectoryName(proj.FilePath) ?? "", StringComparison.OrdinalIgnoreCase))
                    affectedProjects.Add(proj.FilePath);
            }
        }

        return Task.FromResult(new ChangeImpactResult
        {
            ChangedFiles = changedFiles,
            AffectedSymbols = affectedSymbols.ToList(),
            AffectedFiles = affectedFiles.ToList(),
            AffectedProjects = affectedProjects.ToList(),
        });
    }

    public Task<CycleDetectionResult> DetectCyclesAsync(CancellationToken ct)
    {
        using var scope = _store.EnterReadLock();

        var callDag = BuildCallDag();
        var depDag = BuildDependencyDag();

        var callCycles = callDag.FindAllCycles();
        var depCycles = depDag.FindAllCycles();

        return Task.FromResult(new CycleDetectionResult
        {
            CallCycles = callCycles,
            DependencyCycles = depCycles,
            HasCallCycles = callCycles.Count > 0,
            HasDependencyCycles = depCycles.Count > 0,
        });
    }

    public Task<IReadOnlyList<IReadOnlyList<string>>> TopologicalSortByLevelsAsync(CancellationToken ct)
    {
        using var scope = _store.EnterReadLock();
        var dag = BuildCallDag();
        var levels = dag.TopologicalSortByLevels();
        var result = levels.Select(level => (IReadOnlyList<string>)level.Select(n => n.Id).ToList()).ToList();
        return Task.FromResult<IReadOnlyList<IReadOnlyList<string>>>(result);
    }

    private Dag<string> BuildCallDag()
    {
        var dag = new Dag<string>();

        foreach (var kvp in _store.SymbolsByFqn)
        {
            dag.AddNode(new DagNode<string> { Id = kvp.Key, Payload = kvp.Key });
        }

        foreach (var edge in _store.CallEdges)
        {
            if (dag.Nodes.ContainsKey(edge.CallerSymbol) && dag.Nodes.ContainsKey(edge.CalleeSymbol))
            {
                dag.TryAddEdge(new DagEdge
                {
                    FromId = edge.CallerSymbol,
                    ToId = edge.CalleeSymbol,
                    Label = edge.CallKind.ToString(),
                });
            }
        }

        return dag;
    }

    private Dag<string> BuildDependencyDag()
    {
        var dag = new Dag<string>();

        foreach (var kvp in _store.SymbolsByFqn)
        {
            dag.AddNode(new DagNode<string> { Id = kvp.Key, Payload = kvp.Key });
        }

        foreach (var edge in _store.DepEdges)
        {
            if (dag.Nodes.ContainsKey(edge.SourceSymbol) && dag.Nodes.ContainsKey(edge.TargetSymbol))
            {
                dag.TryAddEdge(new DagEdge
                {
                    FromId = edge.SourceSymbol,
                    ToId = edge.TargetSymbol,
                    Label = edge.DependencyKind.ToString(),
                });
            }
        }

        return dag;
    }

    private static Dictionary<string, int> LabelPropagation(
        Dictionary<string, List<CallEdge>> byCaller,
        Dictionary<string, List<CallEdge>> byCallee)
    {
        var allSymbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kvp in byCaller) allSymbols.Add(kvp.Key);
        foreach (var kvp in byCallee) allSymbols.Add(kvp.Key);

        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        var id = 0;
        foreach (var sym in allSymbols)
            labels[sym] = id++;

        for (int iter = 0; iter < 20; iter++)
        {
            var changed = false;
            foreach (var sym in allSymbols)
            {
                var neighborLabels = new List<int>();
                if (byCallee.TryGetValue(sym, out var callers))
                    foreach (var e in callers) neighborLabels.Add(labels.GetValueOrDefault(e.CallerSymbol));
                if (byCaller.TryGetValue(sym, out var callees))
                    foreach (var e in callees) neighborLabels.Add(labels.GetValueOrDefault(e.CalleeSymbol));

                if (neighborLabels.Count == 0) continue;

                var bestLabel = neighborLabels.GroupBy(l => l).OrderByDescending(g => g.Count()).First().Key;
                if (labels[sym] != bestLabel)
                {
                    labels[sym] = bestLabel;
                    changed = true;
                }
            }

            if (!changed) break;
        }

        return labels;
    }

    private static List<CommunityInfo> BuildCommunities(
        Dictionary<string, int> labels,
        Dictionary<string, List<CallEdge>> byCaller,
        Dictionary<string, List<CallEdge>> byCallee)
    {
        var groups = labels.GroupBy(kvp => kvp.Value).ToList();
        var result = new List<CommunityInfo>();

        foreach (var group in groups)
        {
            var members = group.Select(g => g.Key).ToList();
            var memberSet = new HashSet<string>(members, StringComparer.Ordinal);
            var internalEdges = 0;
            var externalEdges = 0;

            foreach (var sym in members)
            {
                if (byCaller.TryGetValue(sym, out var callees))
                {
                    foreach (var edge in callees)
                    {
                        if (memberSet.Contains(edge.CalleeSymbol)) internalEdges++;
                        else externalEdges++;
                    }
                }
            }

            result.Add(new CommunityInfo
            {
                CommunityId = group.Key,
                Members = members,
                MemberCount = members.Count,
                InternalEdges = internalEdges,
                ExternalEdges = externalEdges,
            });
        }

        return result.OrderByDescending(c => c.MemberCount).ToList();
    }

    private string? FindFilePath(string symbolName)
    {
        if (_store.SymbolsByFqn.TryGetValue(symbolName, out var sym))
            return sym.FilePath;
        if (_store.SymbolsByName.TryGetValue(symbolName, out var list) && list.Count > 0)
            return list[0].FilePath;
        return null;
    }

    private static bool IsEntryPoint(SymbolInfo symbol)
    {
        if (symbol.Name is "Main" or "MainAsync" or "Program") return true;
        if (symbol.Name.StartsWith("On", StringComparison.Ordinal) &&
            symbol.Kind == SymbolKind.Method) return true;
        return false;
    }
}
