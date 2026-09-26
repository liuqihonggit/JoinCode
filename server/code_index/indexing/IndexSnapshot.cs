namespace JoinCode.CodeIndex.Persistence;

/// <summary>
/// 不可变索引快照 — 所有索引数据的不可变快照，通过 CAS 原子切换
/// <para>读：volatile 读无锁 O(1)；写：ImmutableInterlocked.Update CAS 原子替换</para>
/// <para>排序索引：FileTrackingKeysSorted/SymbolsSortedByFqn/SymbolsSortedByName 用于 O(log n) 前缀查询</para>
/// <para>反向索引：ProjectRefsByTarget/NuGetRefsByPackage 用于 O(1) 反向查找</para>
/// </summary>
internal sealed record IndexSnapshot {
    // ── 符号索引（O(1) 精确查找）──
    /// <summary>FQN→符号 精确查找索引</summary>
    public required ImmutableDictionary<string, SymbolInfo> SymbolsByFqn { get; init; }
    /// <summary>名称→符号列表 模糊查找索引（同名多符号）</summary>
    public required ImmutableDictionary<string, ImmutableList<SymbolInfo>> SymbolsByName { get; init; }
    /// <summary>文件路径→符号列表 索引</summary>
    public required ImmutableDictionary<string, ImmutableList<SymbolInfo>> SymbolsByFile { get; init; }
    /// <summary>符号种类→符号列表 索引</summary>
    public required ImmutableDictionary<SymbolKind, ImmutableList<SymbolInfo>> SymbolsByKind { get; init; }

    // ── 调用图 ──
    /// <summary>全部调用边</summary>
    public required ImmutableList<CallEdge> CallEdges { get; init; }
    /// <summary>调用方→调用边列表 索引</summary>
    public required ImmutableDictionary<string, ImmutableList<CallEdge>> CallsByCaller { get; init; }
    /// <summary>被调用方→调用边列表 索引</summary>
    public required ImmutableDictionary<string, ImmutableList<CallEdge>> CallsByCallee { get; init; }
    /// <summary>文件路径→调用边列表 索引</summary>
    public required ImmutableDictionary<string, ImmutableList<CallEdge>> CallsByFile { get; init; }

    // ── 依赖图 ──
    /// <summary>全部依赖边</summary>
    public required ImmutableList<DependencyEdge> DepEdges { get; init; }
    /// <summary>源符号→依赖边列表 索引</summary>
    public required ImmutableDictionary<string, ImmutableList<DependencyEdge>> DepsBySource { get; init; }
    /// <summary>目标符号→依赖边列表 索引</summary>
    public required ImmutableDictionary<string, ImmutableList<DependencyEdge>> DepsByTarget { get; init; }
    /// <summary>文件路径→依赖边列表 索引</summary>
    public required ImmutableDictionary<string, ImmutableList<DependencyEdge>> DepsByFile { get; init; }

    // ── 项目依赖 ──
    /// <summary>项目路径→项目信息 索引</summary>
    public required ImmutableDictionary<string, ProjectInfo> Projects { get; init; }
    /// <summary>项目路径→项目引用边列表 索引</summary>
    public required ImmutableDictionary<string, ImmutableList<ProjectReferenceEdge>> ProjectRefs { get; init; }
    /// <summary>项目路径→NuGet 包引用列表 索引</summary>
    public required ImmutableDictionary<string, ImmutableList<NuGetPackageReference>> NuGetRefs { get; init; }

    // ── 文件追踪 ──
    /// <summary>文件路径→文件追踪条目 索引</summary>
    public required ImmutableDictionary<string, FileTrackingEntry> FileTracking { get; init; }

    // ── 排序索引（O(log n) 前缀/范围查询）──
    /// <summary>文件追踪键排序列表（前缀查询用）</summary>
    public required ImmutableList<string> FileTrackingKeysSorted { get; init; }
    /// <summary>符号按 FQN 排序列表（前缀查询用）</summary>
    public required ImmutableList<SymbolInfo> SymbolsSortedByFqn { get; init; }
    /// <summary>符号按名称排序列表（前缀查询用）</summary>
    public required ImmutableList<SymbolInfo> SymbolsSortedByName { get; init; }

    // ── 反向索引（O(1) 反向查找）──
    /// <summary>目标项目路径→项目引用边列表 反向索引</summary>
    public required ImmutableDictionary<string, ImmutableList<ProjectReferenceEdge>> ProjectRefsByTarget { get; init; }
    /// <summary>包名→NuGet 引用列表 反向索引</summary>
    public required ImmutableDictionary<string, ImmutableList<NuGetPackageReference>> NuGetRefsByPackage { get; init; }

    /// <summary>最后更新时间</summary>
    public DateTimeOffset LastUpdated { get; init; }

    /// <summary>空快照</summary>
    public static readonly IndexSnapshot Empty = new() {
        SymbolsByFqn = ImmutableDictionary<string, SymbolInfo>.Empty.WithComparers(StringComparer.Ordinal),
        SymbolsByName = ImmutableDictionary<string, ImmutableList<SymbolInfo>>.Empty.WithComparers(StringComparer.Ordinal),
        SymbolsByFile = ImmutableDictionary<string, ImmutableList<SymbolInfo>>.Empty.WithComparers(StringComparer.Ordinal),
        SymbolsByKind = ImmutableDictionary<SymbolKind, ImmutableList<SymbolInfo>>.Empty,
        CallEdges = ImmutableList<CallEdge>.Empty,
        CallsByCaller = ImmutableDictionary<string, ImmutableList<CallEdge>>.Empty.WithComparers(StringComparer.Ordinal),
        CallsByCallee = ImmutableDictionary<string, ImmutableList<CallEdge>>.Empty.WithComparers(StringComparer.Ordinal),
        CallsByFile = ImmutableDictionary<string, ImmutableList<CallEdge>>.Empty.WithComparers(StringComparer.Ordinal),
        DepEdges = ImmutableList<DependencyEdge>.Empty,
        DepsBySource = ImmutableDictionary<string, ImmutableList<DependencyEdge>>.Empty.WithComparers(StringComparer.Ordinal),
        DepsByTarget = ImmutableDictionary<string, ImmutableList<DependencyEdge>>.Empty.WithComparers(StringComparer.Ordinal),
        DepsByFile = ImmutableDictionary<string, ImmutableList<DependencyEdge>>.Empty.WithComparers(StringComparer.Ordinal),
        Projects = ImmutableDictionary<string, ProjectInfo>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        ProjectRefs = ImmutableDictionary<string, ImmutableList<ProjectReferenceEdge>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        NuGetRefs = ImmutableDictionary<string, ImmutableList<NuGetPackageReference>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        FileTracking = ImmutableDictionary<string, FileTrackingEntry>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        FileTrackingKeysSorted = ImmutableList<string>.Empty,
        SymbolsSortedByFqn = ImmutableList<SymbolInfo>.Empty,
        SymbolsSortedByName = ImmutableList<SymbolInfo>.Empty,
        ProjectRefsByTarget = ImmutableDictionary<string, ImmutableList<ProjectReferenceEdge>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        NuGetRefsByPackage = ImmutableDictionary<string, ImmutableList<NuGetPackageReference>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        LastUpdated = DateTimeOffset.MinValue,
    };

    // ── 写操作 ──

    /// <summary>索引单文件 — 移除旧→插入新→修正→更新追踪</summary>
    public IndexSnapshot IndexFile(string filePath, string contentHash, ExtractionResult extraction, DateTimeOffset now) {
        var snap = this;
        snap = snap.RemoveFileData(filePath);
        snap = snap.InsertSymbols(extraction.Symbols);
        snap = snap.InsertCallEdges(extraction.Calls);
        snap = snap.InsertDependencyEdges(extraction.Dependencies);
        snap = snap.CorrectInheritsToImplements();
        snap = snap.UpsertFileTracking(filePath, contentHash, extraction.Symbols.Count, now);
        return snap with { LastUpdated = now };
    }

    /// <summary>批量索引文件 — 循环移除旧→插入新，最后一次性修正</summary>
    public IndexSnapshot IndexFilesBatch(
        IReadOnlyList<(string FilePath, string Hash, ExtractionResult Extraction)> files,
        DateTimeOffset now) {
        var snap = this;
        foreach (var (filePath, hash, extraction) in files) {
            snap = snap.RemoveFileData(filePath);
            snap = snap.InsertSymbols(extraction.Symbols);
            snap = snap.InsertCallEdges(extraction.Calls);
            snap = snap.InsertDependencyEdges(extraction.Dependencies);
            snap = snap.UpsertFileTracking(filePath, hash, extraction.Symbols.Count, now);
        }
        snap = snap.CorrectInheritsToImplements();
        return snap with { LastUpdated = now };
    }

    /// <summary>移除文件所有索引数据 + 文件追踪</summary>
    public IndexSnapshot RemoveFile(string filePath) {
        var snap = RemoveFileData(filePath);
        var ft = snap.FileTracking;
        if (ft.TryGetValue(filePath, out _)) {
            ft = ft.Remove(filePath);
            snap = snap with { FileTracking = ft, FileTrackingKeysSorted = RebuildSortedKeys(ft) };
        }
        return snap;
    }

    /// <summary>清空全部</summary>
    public static IndexSnapshot Clear() => Empty;

    /// <summary>从持久化数据重建索引快照</summary>
    public static IndexSnapshot Load(GraphPersistenceData data) {
        var snap = Empty;
        snap = snap.InsertSymbols(data.Symbols);
        snap = snap.InsertCallEdges(data.CallEdges);
        snap = snap.InsertDependencyEdges(data.DependencyEdges);

        var projects = snap.Projects;
        foreach (var proj in data.Projects)
            projects = projects.SetItem(proj.FilePath, proj);

        var pr = snap.ProjectRefs;
        var prByTarget = snap.ProjectRefsByTarget;
        foreach (var ref_ in data.ProjectReferences) {
            pr = AddToListIndex(pr, ref_.SourceProjectPath, ref_);
            var normTarget = InMemoryIndexStore.NormalizeKey(ref_.TargetProjectPath);
            prByTarget = AddToListIndex(prByTarget, normTarget, ref_);
        }

        var nr = snap.NuGetRefs;
        var nrByPkg = snap.NuGetRefsByPackage;
        foreach (var pkg in data.NuGetReferences) {
            nr = AddToListIndex(nr, pkg.ProjectPath, pkg);
            nrByPkg = AddToListIndex(nrByPkg, pkg.PackageName, pkg);
        }

        var ft = snap.FileTracking;
        foreach (var ftInfo in data.FileTracking) {
            ft = ft.SetItem(ftInfo.FilePath, new FileTrackingEntry {
                FilePath = ftInfo.FilePath,
                Hash = ftInfo.Hash,
                SymbolCount = ftInfo.SymbolCount,
                LastModified = ftInfo.LastModified
            });
        }

        return snap with {
            Projects = projects,
            ProjectRefs = pr,
            ProjectRefsByTarget = prByTarget,
            NuGetRefs = nr,
            NuGetRefsByPackage = nrByPkg,
            FileTracking = ft,
            FileTrackingKeysSorted = RebuildSortedKeys(ft),
            LastUpdated = data.SavedAt
        };
    }

    /// <summary>索引项目 — 移除旧→插入新</summary>
    public IndexSnapshot IndexProject(
        string filePath,
        ProjectInfo project,
        IReadOnlyList<ProjectReferenceEdge> projectRefs,
        IReadOnlyList<NuGetPackageReference> nuGetRefs) {
        var snap = RemoveProjectData(filePath);
        var projects = snap.Projects.SetItem(filePath, project);

        var pr = snap.ProjectRefs;
        var prByTarget = snap.ProjectRefsByTarget;
        if (projectRefs.Count > 0) {
            pr = pr.SetItem(filePath, projectRefs.ToImmutableList());
            foreach (var edge in projectRefs) {
                var normTarget = InMemoryIndexStore.NormalizeKey(edge.TargetProjectPath);
                prByTarget = prByTarget.SetItem(normTarget,
                    (prByTarget.GetValueOrDefault(normTarget) ?? ImmutableList<ProjectReferenceEdge>.Empty).Add(edge));
            }
        }

        var nr = snap.NuGetRefs;
        var nrByPkg = snap.NuGetRefsByPackage;
        if (nuGetRefs.Count > 0) {
            nr = nr.SetItem(filePath, nuGetRefs.ToImmutableList());
            foreach (var pkg in nuGetRefs) {
                nrByPkg = nrByPkg.SetItem(pkg.PackageName,
                    (nrByPkg.GetValueOrDefault(pkg.PackageName) ?? ImmutableList<NuGetPackageReference>.Empty).Add(pkg));
            }
        }

        return this with { Projects = projects, ProjectRefs = pr, ProjectRefsByTarget = prByTarget, NuGetRefs = nr, NuGetRefsByPackage = nrByPkg };
    }

    /// <summary>移除项目数据</summary>
    public IndexSnapshot RemoveProject(string filePath) => RemoveProjectData(filePath);

    /// <summary>清空项目数据</summary>
    public IndexSnapshot ClearProjects() => this with {
        Projects = ImmutableDictionary<string, ProjectInfo>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        ProjectRefs = ImmutableDictionary<string, ImmutableList<ProjectReferenceEdge>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        NuGetRefs = ImmutableDictionary<string, ImmutableList<NuGetPackageReference>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        ProjectRefsByTarget = ImmutableDictionary<string, ImmutableList<ProjectReferenceEdge>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        NuGetRefsByPackage = ImmutableDictionary<string, ImmutableList<NuGetPackageReference>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
    };

    // ── 内部写操作 ──

    /// <summary>移除文件相关符号/调用边/依赖边（不含文件追踪）</summary>
    private IndexSnapshot RemoveFileData(string filePath) {
        var symbolsByFqn = SymbolsByFqn;
        var symbolsByName = SymbolsByName;
        var symbolsByFile = SymbolsByFile;
        var symbolsByKind = SymbolsByKind;

        if (symbolsByFile.TryGetValue(filePath, out var symbolsInFile)) {
            foreach (var sym in symbolsInFile) {
                symbolsByFqn = symbolsByFqn.Remove(sym.FullyQualifiedName);
                symbolsByName = RemoveFromListIndex(symbolsByName, sym.Name, sym);
                symbolsByKind = RemoveFromListIndex(symbolsByKind, sym.Kind, sym);
            }
            symbolsByFile = symbolsByFile.Remove(filePath);
        }

        var callEdges = CallEdges;
        var callsByCaller = CallsByCaller;
        var callsByCallee = CallsByCallee;
        var callsByFile = CallsByFile;

        if (callsByFile.TryGetValue(filePath, out var callsInFile)) {
            foreach (var edge in callsInFile) {
                callEdges = callEdges.Remove(edge);
                callsByCaller = RemoveFromListIndex(callsByCaller, edge.CallerSymbol, edge);
                callsByCallee = RemoveFromListIndex(callsByCallee, edge.CalleeSymbol, edge);
            }
            callsByFile = callsByFile.Remove(filePath);
        }

        var depEdges = DepEdges;
        var depsBySource = DepsBySource;
        var depsByTarget = DepsByTarget;
        var depsByFile = DepsByFile;

        if (depsByFile.TryGetValue(filePath, out var depsInFile)) {
            foreach (var edge in depsInFile) {
                depEdges = depEdges.Remove(edge);
                depsBySource = RemoveFromListIndex(depsBySource, edge.SourceSymbol, edge);
                depsByTarget = RemoveFromListIndex(depsByTarget, edge.TargetSymbol, edge);
            }
            depsByFile = depsByFile.Remove(filePath);
        }

        var newSymbolsSortedByFqn = RebuildSymbolsSortedByFqn(symbolsByFqn);
        var newSymbolsSortedByName = RebuildSymbolsSortedByName(symbolsByName);

        return this with {
            SymbolsByFqn = symbolsByFqn,
            SymbolsByName = symbolsByName,
            SymbolsByFile = symbolsByFile,
            SymbolsByKind = symbolsByKind,
            CallEdges = callEdges,
            CallsByCaller = callsByCaller,
            CallsByCallee = callsByCallee,
            CallsByFile = callsByFile,
            DepEdges = depEdges,
            DepsBySource = depsBySource,
            DepsByTarget = depsByTarget,
            DepsByFile = depsByFile,
            SymbolsSortedByFqn = newSymbolsSortedByFqn,
            SymbolsSortedByName = newSymbolsSortedByName,
        };
    }

    private IndexSnapshot InsertSymbols(IReadOnlyList<SymbolInfo> symbols) {
        var symbolsByFqn = SymbolsByFqn;
        var symbolsByName = SymbolsByName;
        var symbolsByFile = SymbolsByFile;
        var symbolsByKind = SymbolsByKind;

        foreach (var symbol in symbols) {
            if (symbolsByFqn.TryGetValue(symbol.FullyQualifiedName, out var existing)) {
                symbolsByName = RemoveFromListIndex(symbolsByName, existing.Name, existing);
                symbolsByFile = RemoveFromListIndex(symbolsByFile, existing.FilePath, existing);
                symbolsByKind = RemoveFromListIndex(symbolsByKind, existing.Kind, existing);
            }

            symbolsByFqn = symbolsByFqn.SetItem(symbol.FullyQualifiedName, symbol);
            symbolsByName = AddToListIndex(symbolsByName, symbol.Name, symbol);
            symbolsByFile = AddToListIndex(symbolsByFile, symbol.FilePath, symbol);
            symbolsByKind = AddToListIndex(symbolsByKind, symbol.Kind, symbol);
        }

        return this with {
            SymbolsByFqn = symbolsByFqn,
            SymbolsByName = symbolsByName,
            SymbolsByFile = symbolsByFile,
            SymbolsByKind = symbolsByKind,
            SymbolsSortedByFqn = RebuildSymbolsSortedByFqn(symbolsByFqn),
            SymbolsSortedByName = RebuildSymbolsSortedByName(symbolsByName),
        };
    }

    private IndexSnapshot InsertCallEdges(IReadOnlyList<CallEdge> calls) {
        var callEdges = CallEdges;
        var callsByCaller = CallsByCaller;
        var callsByCallee = CallsByCallee;
        var callsByFile = CallsByFile;

        foreach (var call in calls) {
            callEdges = callEdges.Add(call);
            callsByCaller = AddToListIndex(callsByCaller, call.CallerSymbol, call);
            callsByCallee = AddToListIndex(callsByCallee, call.CalleeSymbol, call);
            callsByFile = AddToListIndex(callsByFile, call.CallSiteFilePath, call);
        }

        return this with {
            CallEdges = callEdges,
            CallsByCaller = callsByCaller,
            CallsByCallee = callsByCallee,
            CallsByFile = callsByFile,
        };
    }

    private IndexSnapshot InsertDependencyEdges(IReadOnlyList<DependencyEdge> deps) {
        var depEdges = DepEdges;
        var depsBySource = DepsBySource;
        var depsByTarget = DepsByTarget;
        var depsByFile = DepsByFile;

        foreach (var dep in deps) {
            depEdges = depEdges.Add(dep);
            depsBySource = AddToListIndex(depsBySource, dep.SourceSymbol, dep);
            depsByTarget = AddToListIndex(depsByTarget, dep.TargetSymbol, dep);
            if (!string.IsNullOrEmpty(dep.SourceFilePath)) {
                depsByFile = AddToListIndex(depsByFile, dep.SourceFilePath!, dep);
            }
        }

        return this with {
            DepEdges = depEdges,
            DepsBySource = depsBySource,
            DepsByTarget = depsByTarget,
            DepsByFile = depsByFile,
        };
    }

    /// <summary>修正 Inherits→Implements：当 target 是接口时替换边</summary>
    private IndexSnapshot CorrectInheritsToImplements() {
        var depEdges = DepEdges;
        var replacements = new Dictionary<DependencyEdge, DependencyEdge>();

        for (var i = 0; i < depEdges.Count; i++) {
            var dep = depEdges[i];
            if (dep.DependencyKind != DependencyKind.Inherits) continue;
            if (!SymbolsByFqn.TryGetValue(dep.TargetSymbol, out var target) || target.Kind != SymbolKind.Interface) continue;

            var newDep = new DependencyEdge {
                SourceSymbol = dep.SourceSymbol,
                TargetSymbol = dep.TargetSymbol,
                DependencyKind = DependencyKind.Implements,
                SourceFilePath = dep.SourceFilePath
            };
            depEdges = depEdges.SetItem(i, newDep);
            replacements[dep] = newDep;
        }

        if (replacements.Count == 0) return this;

        return this with {
            DepEdges = depEdges,
            DepsBySource = ReplaceEdgesInLists(DepsBySource, replacements),
            DepsByTarget = ReplaceEdgesInLists(DepsByTarget, replacements),
            DepsByFile = ReplaceEdgesInLists(DepsByFile, replacements),
        };
    }

    private IndexSnapshot UpsertFileTracking(string filePath, string hash, int symbolCount, DateTimeOffset now) {
        var ft = FileTracking.SetItem(filePath, new FileTrackingEntry {
            FilePath = filePath,
            Hash = hash,
            SymbolCount = symbolCount,
            LastModified = now
        });
        return this with { FileTracking = ft, FileTrackingKeysSorted = RebuildSortedKeys(ft) };
    }

    private IndexSnapshot RemoveProjectData(string filePath) {
        var projects = Projects;
        var pr = ProjectRefs;
        var prByTarget = ProjectRefsByTarget;
        var nr = NuGetRefs;
        var nrByPkg = NuGetRefsByPackage;

        if (pr.TryGetValue(filePath, out var oldRefs)) {
            foreach (var edge in oldRefs) {
                var normTarget = InMemoryIndexStore.NormalizeKey(edge.TargetProjectPath);
                prByTarget = RemoveFromListIndex(prByTarget, normTarget, edge);
            }
            pr = pr.Remove(filePath);
        }
        if (nr.TryGetValue(filePath, out var oldPkgs)) {
            foreach (var pkg in oldPkgs) {
                nrByPkg = RemoveFromListIndex(nrByPkg, pkg.PackageName, pkg);
            }
            nr = nr.Remove(filePath);
        }
        projects = projects.Remove(filePath);

        return this with { Projects = projects, ProjectRefs = pr, ProjectRefsByTarget = prByTarget, NuGetRefs = nr, NuGetRefsByPackage = nrByPkg };
    }

    // ── 不可变集合辅助 ──

    private static ImmutableDictionary<TKey, ImmutableList<T>> AddToListIndex<TKey, T>(
        ImmutableDictionary<TKey, ImmutableList<T>> dict, TKey key, T item) where TKey : notnull {
        var list = dict.GetValueOrDefault(key) ?? ImmutableList<T>.Empty;
        return dict.SetItem(key, list.Add(item));
    }

    private static ImmutableDictionary<TKey, ImmutableList<T>> RemoveFromListIndex<TKey, T>(
        ImmutableDictionary<TKey, ImmutableList<T>> dict, TKey key, T item) where TKey : notnull {
        var list = dict.GetValueOrDefault(key);
        if (list is null) return dict;
        var newList = list.Remove(item);
        return newList.IsEmpty ? dict.Remove(key) : dict.SetItem(key, newList);
    }

    private static ImmutableDictionary<TKey, ImmutableList<DependencyEdge>> ReplaceEdgesInLists<TKey>(
        ImmutableDictionary<TKey, ImmutableList<DependencyEdge>> dict,
        Dictionary<DependencyEdge, DependencyEdge> replacements) where TKey : notnull {
        var builder = dict.ToBuilder();
        var keysToRemove = new List<TKey>();
        foreach (var kv in dict) {
            var list = kv.Value;
            var changed = false;
            for (var i = 0; i < list.Count; i++) {
                if (replacements.TryGetValue(list[i], out var newDep)) {
                    list = list.SetItem(i, newDep);
                    changed = true;
                }
            }
            if (changed) builder[kv.Key] = list;
        }
        return builder.ToImmutable();
    }

    private static ImmutableList<string> RebuildSortedKeys(ImmutableDictionary<string, FileTrackingEntry> ft)
        => ft.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToImmutableList();

    private static ImmutableList<SymbolInfo> RebuildSymbolsSortedByFqn(ImmutableDictionary<string, SymbolInfo> symbols)
        => symbols.Values.OrderBy(s => s.FullyQualifiedName, StringComparer.Ordinal).ToImmutableList();

    private static ImmutableList<SymbolInfo> RebuildSymbolsSortedByName(ImmutableDictionary<string, ImmutableList<SymbolInfo>> symbolsByName)
        => symbolsByName.Values.SelectMany(v => v).OrderBy(s => s.Name, StringComparer.Ordinal).ToImmutableList();
}
