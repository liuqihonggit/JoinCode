namespace JoinCode.CodeIndex.Analytics;

/// <summary>
/// 图分析实现 — 基于 InMemoryIndexStore 的图数据 + Structura DAG 算法
/// 社区检测: 标签传播; 枢纽分析: 度排序; 死代码: 无调用方检测
/// 环检测/拓扑排序: 构建 Dag&lt;string&gt; 委托 Structura 算法
/// </summary>
[Register(typeof(IGraphAnalytics), ServiceLifetime.Singleton)]
public sealed class GraphAnalytics : ServiceEntity, IGraphAnalytics {
    private readonly InMemoryIndexStore _store;

    /// <summary>
    /// 构造图分析器
    /// </summary>
    /// <param name="store">内存索引存储</param>
    public GraphAnalytics(InMemoryIndexStore store) {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// 检测代码社区 — 基于标签传播算法聚类相关符号
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>社区信息列表</returns>
    public Task<IReadOnlyList<CommunityInfo>> DetectCommunitiesAsync(CancellationToken ct) {
        var snap = _store.GetSnapshot();
        return Task.FromResult<IReadOnlyList<CommunityInfo>>(DetectCommunities(snap));
    }

    /// <summary>
    /// 检测代码社区 — 内部静态方法，基于标签传播算法
    /// </summary>
    /// <param name="snap">索引快照</param>
    /// <returns>社区信息列表</returns>
    internal static List<CommunityInfo> DetectCommunities(IndexSnapshot snap) {
        var labels = LabelPropagation(snap.CallsByCaller, snap.CallsByCallee);
        return BuildCommunities(labels, snap.CallsByCaller, snap.CallsByCallee);
    }

    /// <summary>
    /// 获取枢纽节点 — 按总度数（入度+出度）降序排列取前 N 个
    /// </summary>
    /// <param name="topN">返回的节点数量</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>枢纽节点信息列表</returns>
    public Task<IReadOnlyList<HubNodeInfo>> GetHubNodesAsync(int topN, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(topN < 1 ? null : nameof(topN));
        var snap = _store.GetSnapshot();

        var degreeMap = new Dictionary<string, (int In, int Out)>(StringComparer.Ordinal);

        foreach (var kvp in snap.CallsByCallee) {
            var sym = kvp.Key;
            var current = degreeMap.GetValueOrDefault(sym);
            degreeMap[sym] = (current.In + kvp.Value.Count, current.Out);
        }

        foreach (var kvp in snap.CallsByCaller) {
            var sym = kvp.Key;
            var current = degreeMap.GetValueOrDefault(sym);
            degreeMap[sym] = (current.In, current.Out + kvp.Value.Count);
        }

        var hubs = degreeMap
            .Select(kvp => new HubNodeInfo {
                SymbolName = kvp.Key,
                InDegree = kvp.Value.In,
                OutDegree = kvp.Value.Out,
                TotalDegree = kvp.Value.In + kvp.Value.Out,
                FilePath = FindFilePath(snap, kvp.Key),
            })
            .OrderByDescending(h => h.TotalDegree)
            .Take(topN)
            .ToList();

        return Task.FromResult<IReadOnlyList<HubNodeInfo>>(hubs);
    }

    /// <summary>
    /// 检测死代码 — 查找无调用方的非公开方法/局部函数
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>死代码条目列表</returns>
    public Task<IReadOnlyList<DeadCodeEntry>> DetectDeadCodeAsync(CancellationToken ct) {
        var snap = _store.GetSnapshot();
        var dead = new List<DeadCodeEntry>();

        foreach (var kvp in snap.SymbolsByFqn) {
            var symbol = kvp.Value;
            if (symbol.Kind != SymbolKind.Method && symbol.Kind != SymbolKind.LocalFunction)
                continue;

            if (symbol.Accessibility == "public" || symbol.Accessibility == "internal")
                continue;

            if (IsEntryPoint(symbol))
                continue;

            if (!snap.CallsByCallee.ContainsKey(symbol.FullyQualifiedName) &&
                !snap.CallsByCallee.ContainsKey(symbol.Name)) {
                dead.Add(new DeadCodeEntry {
                    SymbolName = symbol.FullyQualifiedName,
                    FilePath = symbol.FilePath,
                    Line = symbol.StartLine,
                    Reason = "No callers found in index",
                });
            }
        }

        return Task.FromResult<IReadOnlyList<DeadCodeEntry>>(dead);
    }

    /// <summary>
    /// 提取子图 — 以指定符号为中心，按跳数扩展收集相关节点与边
    /// </summary>
    /// <param name="centerSymbol">中心符号全限定名</param>
    /// <param name="hops">扩展跳数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>子图结果</returns>
    public Task<SubgraphResult> ExtractSubgraphAsync(string centerSymbol, int hops, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(centerSymbol);
        var snap = _store.GetSnapshot();

        var nodes = new HashSet<string>(StringComparer.Ordinal) { centerSymbol };
        var edges = new List<CallEdge>();
        var edgeSet = new HashSet<CallEdge>();
        var frontier = new HashSet<string>(StringComparer.Ordinal) { centerSymbol };

        for (var i = 0; i < hops && frontier.Count > 0; i++) {
            var nextFrontier = new HashSet<string>(StringComparer.Ordinal);

            foreach (var sym in frontier) {
                if (snap.CallsByCaller.TryGetValue(sym, out var callees)) {
                    foreach (var edge in callees) {
                        if (edgeSet.Add(edge))
                            edges.Add(edge);
                        if (nodes.Add(edge.CalleeSymbol))
                            nextFrontier.Add(edge.CalleeSymbol);
                    }
                }

                if (snap.CallsByCallee.TryGetValue(sym, out var callers)) {
                    foreach (var edge in callers) {
                        if (edgeSet.Add(edge))
                            edges.Add(edge);
                        if (nodes.Add(edge.CallerSymbol))
                            nextFrontier.Add(edge.CallerSymbol);
                    }
                }
            }

            frontier = nextFrontier;
        }

        return Task.FromResult(new SubgraphResult {
            CenterSymbol = centerSymbol,
            Hops = hops,
            Nodes = nodes.ToList(),
            Edges = edges,
        });
    }

    /// <summary>
    /// 分析变更影响 — 沿调用图反向传播，找出受影响的所有符号、文件和项目
    /// </summary>
    /// <param name="changedFiles">变更文件列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>变更影响结果</returns>
    public Task<ChangeImpactResult> AnalyzeChangeImpactAsync(IReadOnlyList<string> changedFiles, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(changedFiles);
        var snap = _store.GetSnapshot();

        var affectedSymbols = new HashSet<string>(StringComparer.Ordinal);
        var affectedFiles = new HashSet<string>(changedFiles, StringComparer.Ordinal);
        var queue = new Queue<string>();

        foreach (var file in changedFiles) {
            if (!snap.SymbolsByFile.TryGetValue(file, out var symbols)) continue;
            foreach (var sym in symbols) {
                affectedSymbols.Add(sym.FullyQualifiedName);
                queue.Enqueue(sym.FullyQualifiedName);
            }
        }

        while (queue.Count > 0) {
            var current = queue.Dequeue();
            if (!snap.CallsByCallee.TryGetValue(current, out var callers)) continue;

            foreach (var edge in callers) {
                if (affectedSymbols.Add(edge.CallerSymbol)) {
                    queue.Enqueue(edge.CallerSymbol);
                    if (!string.IsNullOrEmpty(edge.CallSiteFilePath))
                        affectedFiles.Add(edge.CallSiteFilePath);
                }
            }
        }

        var affectedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in affectedFiles) {
            foreach (var proj in snap.Projects.Values) {
                if (file.StartsWith(Path.GetDirectoryName(proj.FilePath) ?? "", StringComparison.OrdinalIgnoreCase))
                    affectedProjects.Add(proj.FilePath);
            }
        }

        return Task.FromResult(new ChangeImpactResult {
            ChangedFiles = changedFiles,
            AffectedSymbols = affectedSymbols.ToList(),
            AffectedFiles = affectedFiles.ToList(),
            AffectedProjects = affectedProjects.ToList(),
        });
    }

    /// <summary>
    /// 检测环 — 分别检测调用环和依赖环
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>环检测结果</returns>
    public Task<CycleDetectionResult> DetectCyclesAsync(CancellationToken ct) {
        var snap = _store.GetSnapshot();

        var callDag = BuildCallDag(snap);
        var depDag = BuildDependencyDag(snap);

        var callCycles = callDag.FindAllCycles();
        var depCycles = depDag.FindAllCycles();

        return Task.FromResult(new CycleDetectionResult {
            CallCycles = callCycles,
            DependencyCycles = depCycles,
            HasCallCycles = callCycles.Count > 0,
            HasDependencyCycles = depCycles.Count > 0,
        });
    }

    /// <summary>
    /// 按层级拓扑排序 — 返回每层可并行执行的符号列表
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>按层分组的符号列表</returns>
    public Task<IReadOnlyList<IReadOnlyList<string>>> TopologicalSortByLevelsAsync(CancellationToken ct) {
        var snap = _store.GetSnapshot();
        var dag = BuildCallDag(snap);
        var levels = dag.TopologicalSortByLevels();
        var result = levels.Select(level => (IReadOnlyList<string>)level.Select(n => n.Id).ToList()).ToList();
        return Task.FromResult<IReadOnlyList<IReadOnlyList<string>>>(result);
    }

    private static Dag<string> BuildCallDag(IndexSnapshot snap) {
        var dag = new Dag<string>();

        foreach (var kvp in snap.SymbolsByFqn) {
            dag.AddNode(new DagNode<string> { Id = kvp.Key, Payload = kvp.Key });
        }

        // Ensure nodes exist for symbols referenced only in CallEdges
        foreach (var edge in snap.CallEdges) {
            if (!dag.Nodes.ContainsKey(edge.CallerSymbol))
                dag.AddNode(new DagNode<string> { Id = edge.CallerSymbol, Payload = edge.CallerSymbol });
            if (!dag.Nodes.ContainsKey(edge.CalleeSymbol))
                dag.AddNode(new DagNode<string> { Id = edge.CalleeSymbol, Payload = edge.CalleeSymbol });
        }

        foreach (var edge in snap.CallEdges) {
            dag.TryAddEdge(new DagEdge {
                FromId = edge.CallerSymbol,
                ToId = edge.CalleeSymbol,
                Label = edge.CallKind.ToString(),
            });
        }

        return dag;
    }

    private static Dag<string> BuildDependencyDag(IndexSnapshot snap) {
        var dag = new Dag<string>();

        foreach (var kvp in snap.SymbolsByFqn) {
            dag.AddNode(new DagNode<string> { Id = kvp.Key, Payload = kvp.Key });
        }

        // Ensure nodes exist for symbols referenced only in DepEdges
        foreach (var edge in snap.DepEdges) {
            if (!dag.Nodes.ContainsKey(edge.SourceSymbol))
                dag.AddNode(new DagNode<string> { Id = edge.SourceSymbol, Payload = edge.SourceSymbol });
            if (!dag.Nodes.ContainsKey(edge.TargetSymbol))
                dag.AddNode(new DagNode<string> { Id = edge.TargetSymbol, Payload = edge.TargetSymbol });
        }

        foreach (var edge in snap.DepEdges) {
            dag.TryAddEdge(new DagEdge {
                FromId = edge.SourceSymbol,
                ToId = edge.TargetSymbol,
                Label = edge.DependencyKind.ToString(),
            });
        }

        return dag;
    }

    /// <summary>
    /// 标签传播算法 — 基于调用关系迭代传播社区标签
    /// </summary>
    /// <param name="byCaller">按调用方分组的调用边</param>
    /// <param name="byCallee">按被调用方分组的调用边</param>
    /// <returns>符号到社区标签的映射</returns>
    internal static Dictionary<string, int> LabelPropagation(
        ImmutableDictionary<string, ImmutableList<CallEdge>> byCaller,
        ImmutableDictionary<string, ImmutableList<CallEdge>> byCallee) {
        var allSymbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kvp in byCaller) allSymbols.Add(kvp.Key);
        foreach (var kvp in byCallee) allSymbols.Add(kvp.Key);

        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        var id = 0;
        foreach (var sym in allSymbols)
            labels[sym] = id++;

        for (var iter = 0; iter < 20; iter++) {
            var changed = false;
            foreach (var sym in allSymbols) {
                var neighborLabels = new List<int>();
                if (byCallee.TryGetValue(sym, out var callers))
                    foreach (var e in callers) neighborLabels.Add(labels.GetValueOrDefault(e.CallerSymbol));
                if (byCaller.TryGetValue(sym, out var callees))
                    foreach (var e in callees) neighborLabels.Add(labels.GetValueOrDefault(e.CalleeSymbol));

                if (neighborLabels.Count == 0) continue;

                var bestLabel = neighborLabels.GroupBy(l => l).OrderByDescending(g => g.Count()).First().Key;
                if (labels[sym] != bestLabel) {
                    labels[sym] = bestLabel;
                    changed = true;
                }
            }

            if (!changed) break;
        }

        return labels;
    }

    /// <summary>
    /// 构建社区列表 — 按标签分组并统计内部/外部边数
    /// </summary>
    /// <param name="labels">符号到社区标签的映射</param>
    /// <param name="byCaller">按调用方分组的调用边</param>
    /// <param name="byCallee">按被调用方分组的调用边</param>
    /// <returns>社区信息列表</returns>
    internal static List<CommunityInfo> BuildCommunities(
        Dictionary<string, int> labels,
        ImmutableDictionary<string, ImmutableList<CallEdge>> byCaller,
        ImmutableDictionary<string, ImmutableList<CallEdge>> byCallee) {
        var groups = labels.GroupBy(kvp => kvp.Value).ToList();
        var result = new List<CommunityInfo>();

        foreach (var group in groups) {
            var members = group.Select(g => g.Key).ToList();
            var memberSet = new HashSet<string>(members, StringComparer.Ordinal);
            var internalEdges = 0;
            var externalEdges = 0;

            foreach (var sym in members) {
                if (byCaller.TryGetValue(sym, out var callees)) {
                    foreach (var edge in callees) {
                        if (memberSet.Contains(edge.CalleeSymbol)) internalEdges++;
                        else externalEdges++;
                    }
                }
            }

            result.Add(new CommunityInfo {
                CommunityId = group.Key,
                Members = members,
                MemberCount = members.Count,
                InternalEdges = internalEdges,
                ExternalEdges = externalEdges,
            });
        }

        return result.OrderByDescending(c => c.MemberCount).ToList();
    }

    private static string? FindFilePath(IndexSnapshot snap, string symbolName) {
        if (snap.SymbolsByFqn.TryGetValue(symbolName, out var sym))
            return sym.FilePath;
        if (snap.SymbolsByName.TryGetValue(symbolName, out var list) && list.Count > 0)
            return list[0].FilePath;
        return null;
    }

    /// <summary>
    /// 查询图 — 按关键词匹配符号名/全限定名/文件路径/命名空间并评分排序
    /// </summary>
    /// <param name="query">查询字符串 — 空格分词</param>
    /// <param name="maxResults">最大返回结果数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>图查询结果</returns>
    public Task<GraphQueryResult> QueryAsync(string query, int maxResults, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(query);
        if (maxResults < 1) maxResults = 20;

        var snap = _store.GetSnapshot();

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) {
            return Task.FromResult(new GraphQueryResult {
                Query = query,
                Matches = [],
                TotalMatches = 0,
            });
        }

        var scored = new Dictionary<string, (SymbolInfo Symbol, int Score)>(StringComparer.Ordinal);

        foreach (var kvp in snap.SymbolsByFqn) {
            var symbol = kvp.Value;
            var score = 0;
            var fqn = symbol.FullyQualifiedName;
            var name = symbol.Name;

            foreach (var token in tokens) {
                if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score += 10;
                if (fqn.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score += 5;
                if (symbol.FilePath.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score += 3;
                if (symbol.Namespace is not null && symbol.Namespace.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score += 2;
            }

            if (score > 0)
                scored[fqn] = (symbol, score);
        }

        foreach (var kvp in snap.SymbolsByName) {
            foreach (var token in tokens) {
                if (!kvp.Key.Contains(token, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var symbol in kvp.Value) {
                    var fqn = symbol.FullyQualifiedName;
                    if (!scored.TryGetValue(fqn, out var existing)) continue;
                    scored[fqn] = (existing.Symbol, existing.Score + 4);
                }
            }
        }

        var sorted = scored.Values.ToList();
        sorted.Sort((a, b) => b.Score.CompareTo(a.Score));

        var matches = new List<GraphQueryMatch>();
        foreach (var (symbol, score) in sorted.Take(maxResults)) {
            var related = new List<string>();
            if (snap.CallsByCaller.TryGetValue(symbol.FullyQualifiedName, out var callees))
                related.AddRange(callees.Select(e => e.CalleeSymbol).Take(5));
            if (snap.CallsByCallee.TryGetValue(symbol.FullyQualifiedName, out var callers))
                related.AddRange(callers.Select(e => e.CallerSymbol).Take(5));

            matches.Add(new GraphQueryMatch {
                SymbolName = symbol.FullyQualifiedName,
                FilePath = symbol.FilePath,
                Kind = symbol.Kind.ToString(),
                RelevanceScore = score,
                RelatedSymbols = related.Distinct(StringComparer.Ordinal).Take(10).ToList(),
            });
        }

        return Task.FromResult(new GraphQueryResult {
            Query = query,
            Matches = matches,
            TotalMatches = sorted.Count,
        });
    }

    /// <summary>
    /// 查找符号间路径 — 双向 BFS 搜索调用图
    /// </summary>
    /// <param name="fromSymbol">起始符号全限定名</param>
    /// <param name="toSymbol">目标符号全限定名</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>路径查找结果</returns>
    public Task<GraphPathResult> FindPathAsync(string fromSymbol, string toSymbol, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(fromSymbol);
        ArgumentNullException.ThrowIfNull(toSymbol);

        var snap = _store.GetSnapshot();

        if (string.Equals(fromSymbol, toSymbol, StringComparison.Ordinal)) {
            return Task.FromResult(new GraphPathResult {
                FromSymbol = fromSymbol,
                ToSymbol = toSymbol,
                PathFound = true,
                PathNodes = [fromSymbol],
                PathEdges = [],
                PathLength = 0,
            });
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { fromSymbol };
        var predecessor = new Dictionary<string, (string FromSymbol, CallEdge Edge)>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(fromSymbol);

        while (queue.Count > 0) {
            var current = queue.Dequeue();

            if (snap.CallsByCaller.TryGetValue(current, out var callees)) {
                foreach (var edge in callees) {
                    if (!visited.Add(edge.CalleeSymbol)) continue;
                    predecessor[edge.CalleeSymbol] = (current, edge);
                    if (string.Equals(edge.CalleeSymbol, toSymbol, StringComparison.Ordinal))
                        goto PathFound;
                    queue.Enqueue(edge.CalleeSymbol);
                }
            }

            if (snap.CallsByCallee.TryGetValue(current, out var callers)) {
                foreach (var edge in callers) {
                    if (!visited.Add(edge.CallerSymbol)) continue;
                    predecessor[edge.CallerSymbol] = (current, edge);
                    if (string.Equals(edge.CallerSymbol, toSymbol, StringComparison.Ordinal))
                        goto PathFound;
                    queue.Enqueue(edge.CallerSymbol);
                }
            }
        }

        return Task.FromResult(new GraphPathResult {
            FromSymbol = fromSymbol,
            ToSymbol = toSymbol,
            PathFound = false,
            PathNodes = [],
            PathEdges = [],
            PathLength = -1,
        });

    PathFound:
        var pathNodes = new List<string>();
        var pathEdges = new List<CallEdge>();
        var step = toSymbol;
        while (!string.Equals(step, fromSymbol, StringComparison.Ordinal)) {
            pathNodes.Add(step);
            var (prev, edge) = predecessor[step];
            pathEdges.Add(edge);
            step = prev;
        }
        pathNodes.Add(fromSymbol);
        pathNodes.Reverse();
        pathEdges.Reverse();

        return Task.FromResult(new GraphPathResult {
            FromSymbol = fromSymbol,
            ToSymbol = toSymbol,
            PathFound = true,
            PathNodes = pathNodes,
            PathEdges = pathEdges,
            PathLength = pathEdges.Count,
        });
    }

    /// <summary>
    /// 解释符号 — 汇总调用方、被调用方、同文件、同社区等上下文信息
    /// </summary>
    /// <param name="symbolName">符号名或全限定名</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>符号解释结果</returns>
    public Task<GraphExplainResult> ExplainAsync(string symbolName, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(symbolName);

        var snap = _store.GetSnapshot();

        SymbolInfo? symbol = null;
        if (snap.SymbolsByFqn.TryGetValue(symbolName, out var fqnSymbol))
            symbol = fqnSymbol;
        else if (snap.SymbolsByName.TryGetValue(symbolName, out var nameList) && nameList.Count > 0)
            symbol = nameList[0];

        var fqn = symbol?.FullyQualifiedName ?? symbolName;

        var callers = new List<string>();
        if (snap.CallsByCallee.TryGetValue(fqn, out var callerEdges))
            callers = callerEdges.Select(e => e.CallerSymbol).Distinct(StringComparer.Ordinal).ToList();

        var callees = new List<string>();
        if (snap.CallsByCaller.TryGetValue(fqn, out var calleeEdges))
            callees = calleeEdges.Select(e => e.CalleeSymbol).Distinct(StringComparer.Ordinal).ToList();

        var sameFile = new List<string>();
        if (symbol is not null && snap.SymbolsByFile.TryGetValue(symbol.FilePath, out var fileSymbols))
            sameFile = fileSymbols
                .Where(s => s.FullyQualifiedName != fqn)
                .Select(s => s.FullyQualifiedName)
                .Take(20)
                .ToList();

        var sameCommunity = new List<string>();
        var labels = LabelPropagation(snap.CallsByCaller, snap.CallsByCallee);
        if (labels.TryGetValue(fqn, out var communityId)) {
            sameCommunity = labels
                .Where(kvp => kvp.Value == communityId && kvp.Key != fqn)
                .Select(kvp => kvp.Key)
                .Take(20)
                .ToList();
        }

        return Task.FromResult(new GraphExplainResult {
            SymbolName = fqn,
            FilePath = symbol?.FilePath ?? "",
            Kind = symbol?.Kind.ToString() ?? "Unknown",
            Namespace = symbol?.Namespace,
            Callers = callers,
            Callees = callees,
            SameCommunity = sameCommunity,
            SameFile = sameFile,
            InDegree = callers.Count,
            OutDegree = callees.Count,
        });
    }

    private static bool IsEntryPoint(SymbolInfo symbol) {
        if (symbol.Name is "Main" or "MainAsync" or "Program") return true;
        if (symbol.Kind == SymbolKind.Method &&
            symbol.Name.StartsWith("On", StringComparison.Ordinal)) return true;
        return false;
    }
}