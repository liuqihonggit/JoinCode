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
    public required ImmutableHamT<string, SymbolInfo> SymbolsByFqn { get; init; }
    /// <summary>名称→符号列表 模糊查找索引（同名多符号）</summary>
    public required ImmutableHamT<string, ImmutableList<SymbolInfo>> SymbolsByName { get; init; }
    /// <summary>文件路径→符号列表 索引</summary>
    public required ImmutableHamT<string, ImmutableList<SymbolInfo>> SymbolsByFile { get; init; }
    /// <summary>符号种类→符号列表 索引</summary>
    public required ImmutableHamT<SymbolKind, ImmutableList<SymbolInfo>> SymbolsByKind { get; init; }

    // ── 调用图 ──
    /// <summary>全部调用边</summary>
    public required ImmutableList<CallEdge> CallEdges { get; init; }
    /// <summary>调用方→调用边列表 索引</summary>
    public required ImmutableHamT<string, ImmutableList<CallEdge>> CallsByCaller { get; init; }
    /// <summary>被调用方→调用边列表 索引</summary>
    public required ImmutableHamT<string, ImmutableList<CallEdge>> CallsByCallee { get; init; }
    /// <summary>文件路径→调用边列表 索引</summary>
    public required ImmutableHamT<string, ImmutableList<CallEdge>> CallsByFile { get; init; }

    // ── 依赖图 ──
    /// <summary>全部依赖边</summary>
    public required ImmutableList<DependencyEdge> DepEdges { get; init; }
    /// <summary>源符号→依赖边列表 索引</summary>
    public required ImmutableHamT<string, ImmutableList<DependencyEdge>> DepsBySource { get; init; }
    /// <summary>目标符号→依赖边列表 索引</summary>
    public required ImmutableHamT<string, ImmutableList<DependencyEdge>> DepsByTarget { get; init; }
    /// <summary>文件路径→依赖边列表 索引</summary>
    public required ImmutableHamT<string, ImmutableList<DependencyEdge>> DepsByFile { get; init; }

    // ── 项目依赖 ──
    /// <summary>项目路径→项目信息 索引</summary>
    public required ImmutableHamT<string, ProjectInfo> Projects { get; init; }
    /// <summary>项目路径→项目引用边列表 索引</summary>
    public required ImmutableHamT<string, ImmutableList<ProjectReferenceEdge>> ProjectRefs { get; init; }
    /// <summary>项目路径→NuGet 包引用列表 索引</summary>
    public required ImmutableHamT<string, ImmutableList<NuGetPackageReference>> NuGetRefs { get; init; }

    // ── 文件追踪 ──
    /// <summary>文件路径→文件追踪条目 索引</summary>
    public required ImmutableHamT<string, FileTrackingEntry> FileTracking { get; init; }

    // ── 排序索引（O(log n) 前缀/范围查询）──
    /// <summary>文件追踪键排序列表（前缀查询用）</summary>
    public required ImmutableList<string> FileTrackingKeysSorted { get; init; }
    /// <summary>符号按 FQN 排序列表（前缀查询用）</summary>
    public required ImmutableList<SymbolInfo> SymbolsSortedByFqn { get; init; }
    /// <summary>符号按名称排序列表（前缀查询用）</summary>
    public required ImmutableList<SymbolInfo> SymbolsSortedByName { get; init; }

    // ── 反向索引（O(1) 反向查找）──
    /// <summary>目标项目路径→项目引用边列表 反向索引</summary>
    public required ImmutableHamT<string, ImmutableList<ProjectReferenceEdge>> ProjectRefsByTarget { get; init; }
    /// <summary>包名→NuGet 引用列表 反向索引</summary>
    public required ImmutableHamT<string, ImmutableList<NuGetPackageReference>> NuGetRefsByPackage { get; init; }

    /// <summary>最后更新时间</summary>
    public DateTimeOffset LastUpdated { get; init; }

    /// <summary>空快照</summary>
    public static readonly IndexSnapshot Empty = new() {
        SymbolsByFqn = ImmutableHamT<string, SymbolInfo>.Empty.WithComparers(StringComparer.Ordinal),
        SymbolsByName = ImmutableHamT<string, ImmutableList<SymbolInfo>>.Empty.WithComparers(StringComparer.Ordinal),
        SymbolsByFile = ImmutableHamT<string, ImmutableList<SymbolInfo>>.Empty.WithComparers(StringComparer.Ordinal),
        SymbolsByKind = ImmutableHamT<SymbolKind, ImmutableList<SymbolInfo>>.Empty,
        CallEdges = ImmutableList<CallEdge>.Empty,
        CallsByCaller = ImmutableHamT<string, ImmutableList<CallEdge>>.Empty.WithComparers(StringComparer.Ordinal),
        CallsByCallee = ImmutableHamT<string, ImmutableList<CallEdge>>.Empty.WithComparers(StringComparer.Ordinal),
        CallsByFile = ImmutableHamT<string, ImmutableList<CallEdge>>.Empty.WithComparers(StringComparer.Ordinal),
        DepEdges = ImmutableList<DependencyEdge>.Empty,
        DepsBySource = ImmutableHamT<string, ImmutableList<DependencyEdge>>.Empty.WithComparers(StringComparer.Ordinal),
        DepsByTarget = ImmutableHamT<string, ImmutableList<DependencyEdge>>.Empty.WithComparers(StringComparer.Ordinal),
        DepsByFile = ImmutableHamT<string, ImmutableList<DependencyEdge>>.Empty.WithComparers(StringComparer.Ordinal),
        Projects = ImmutableHamT<string, ProjectInfo>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        ProjectRefs = ImmutableHamT<string, ImmutableList<ProjectReferenceEdge>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        NuGetRefs = ImmutableHamT<string, ImmutableList<NuGetPackageReference>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        FileTracking = ImmutableHamT<string, FileTrackingEntry>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        FileTrackingKeysSorted = ImmutableList<string>.Empty,
        SymbolsSortedByFqn = ImmutableList<SymbolInfo>.Empty,
        SymbolsSortedByName = ImmutableList<SymbolInfo>.Empty,
        ProjectRefsByTarget = ImmutableHamT<string, ImmutableList<ProjectReferenceEdge>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        NuGetRefsByPackage = ImmutableHamT<string, ImmutableList<NuGetPackageReference>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
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

    /// <summary>批量索引文件 — 循环移除旧→插入新，最后一次性修正+重建排序</summary>
    public IndexSnapshot IndexFilesBatch(
        IReadOnlyList<(string FilePath, string Hash, ExtractionResult Extraction)> files,
        DateTimeOffset now) {
        var snap = this;
        foreach (var (filePath, hash, extraction) in files) {
            snap = snap.RemoveFileData(filePath, rebuildSorted: false);
            snap = snap.InsertSymbols(extraction.Symbols, rebuildSorted: false, isRebuild: false);
            snap = snap.InsertCallEdges(extraction.Calls);
            snap = snap.InsertDependencyEdges(extraction.Dependencies);
            snap = snap.UpsertFileTracking(filePath, hash, extraction.Symbols.Count, now, rebuildSorted: false);
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
        Projects = ImmutableHamT<string, ProjectInfo>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        ProjectRefs = ImmutableHamT<string, ImmutableList<ProjectReferenceEdge>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        NuGetRefs = ImmutableHamT<string, ImmutableList<NuGetPackageReference>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        ProjectRefsByTarget = ImmutableHamT<string, ImmutableList<ProjectReferenceEdge>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        NuGetRefsByPackage = ImmutableHamT<string, ImmutableList<NuGetPackageReference>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
    };

    // ── 内部写操作 ──

    /// <summary>移除文件相关符号/调用边/依赖边（不含文件追踪）</summary>
    internal IndexSnapshot RemoveFileData(string filePath, bool rebuildSorted = true) {
        var (symbolsByFqn, symbolsByName, symbolsByFile, symbolsByKind) = RemoveSymbolsOfFile(filePath);
        var (callEdges, callsByCaller, callsByCallee, callsByFile) = RemoveCallEdgesOfFile(filePath);
        var (depEdges, depsBySource, depsByTarget, depsByFile) = RemoveDepEdgesOfFile(filePath);

        if (!rebuildSorted) {
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
            };
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

    /// <summary>移除文件相关符号索引（ByFqn/ByName/ByFile/ByKind 四索引同步）</summary>
    internal (ImmutableHamT<string, SymbolInfo> ByFqn,
              ImmutableHamT<string, ImmutableList<SymbolInfo>> ByName,
              ImmutableHamT<string, ImmutableList<SymbolInfo>> ByFile,
              ImmutableHamT<SymbolKind, ImmutableList<SymbolInfo>> ByKind) RemoveSymbolsOfFile(string filePath) {
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

        return (symbolsByFqn, symbolsByName, symbolsByFile, symbolsByKind);
    }

    /// <summary>移除文件相关调用边索引（Edges/ByCaller/ByCallee/ByFile 四索引同步）</summary>
    internal (ImmutableList<CallEdge> Edges,
              ImmutableHamT<string, ImmutableList<CallEdge>> ByCaller,
              ImmutableHamT<string, ImmutableList<CallEdge>> ByCallee,
              ImmutableHamT<string, ImmutableList<CallEdge>> ByFile) RemoveCallEdgesOfFile(string filePath) {
        var callEdges = CallEdges;
        var callsByCaller = CallsByCaller;
        var callsByCallee = CallsByCallee;
        var callsByFile = CallsByFile;

        if (callsByFile.TryGetValue(filePath, out var callsInFile)) {
            var edgeSet = callsInFile.ToHashSet();
            callEdges = callEdges.RemoveAll(e => edgeSet.Contains(e));
            foreach (var edge in callsInFile) {
                callsByCaller = RemoveFromListIndex(callsByCaller, edge.CallerSymbol, edge);
                callsByCallee = RemoveFromListIndex(callsByCallee, edge.CalleeSymbol, edge);
            }
            callsByFile = callsByFile.Remove(filePath);
        }

        return (callEdges, callsByCaller, callsByCallee, callsByFile);
    }

    /// <summary>移除文件相关依赖边索引（Edges/BySource/ByTarget/ByFile 四索引同步）</summary>
    internal (ImmutableList<DependencyEdge> Edges,
              ImmutableHamT<string, ImmutableList<DependencyEdge>> BySource,
              ImmutableHamT<string, ImmutableList<DependencyEdge>> ByTarget,
              ImmutableHamT<string, ImmutableList<DependencyEdge>> ByFile) RemoveDepEdgesOfFile(string filePath) {
        var depEdges = DepEdges;
        var depsBySource = DepsBySource;
        var depsByTarget = DepsByTarget;
        var depsByFile = DepsByFile;

        if (depsByFile.TryGetValue(filePath, out var depsInFile)) {
            var edgeSet = depsInFile.ToHashSet();
            depEdges = depEdges.RemoveAll(e => edgeSet.Contains(e));
            foreach (var edge in depsInFile) {
                depsBySource = RemoveFromListIndex(depsBySource, edge.SourceSymbol, edge);
                depsByTarget = RemoveFromListIndex(depsByTarget, edge.TargetSymbol, edge);
            }
            depsByFile = depsByFile.Remove(filePath);
        }

        return (depEdges, depsBySource, depsByTarget, depsByFile);
    }

    internal IndexSnapshot InsertSymbols(IReadOnlyList<SymbolInfo> symbols, bool rebuildSorted = true, bool isRebuild = true) {
        if (symbols.Count == 0) return this;

        var symbolsByFqn = SymbolsByFqn;
        ImmutableHamT<string, ImmutableList<SymbolInfo>> symbolsByName, symbolsByFile;
        ImmutableHamT<SymbolKind, ImmutableList<SymbolInfo>> symbolsByKind;

        if (isRebuild) {
            symbolsByName = SymbolsByName;
            symbolsByFile = SymbolsByFile;
            symbolsByKind = SymbolsByKind;

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
        } else {
            foreach (var symbol in symbols) {
                symbolsByFqn = symbolsByFqn.SetItem(symbol.FullyQualifiedName, symbol);
            }
            symbolsByName = AddBatchToListIndex(SymbolsByName, symbols, s => s.Name);
            symbolsByFile = AddBatchToListIndex(SymbolsByFile, symbols, s => s.FilePath);
            symbolsByKind = AddBatchToListIndex(SymbolsByKind, symbols, s => s.Kind);
        }

        if (!rebuildSorted) {
            return this with {
                SymbolsByFqn = symbolsByFqn,
                SymbolsByName = symbolsByName,
                SymbolsByFile = symbolsByFile,
                SymbolsByKind = symbolsByKind,
            };
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

    internal IndexSnapshot InsertCallEdges(IReadOnlyList<CallEdge> calls) {
        if (calls.Count == 0) return this;

        var callEdges = CallEdges.AddRange(calls);
        var callsByCaller = AddBatchToListIndex(CallsByCaller, calls, c => c.CallerSymbol);
        var callsByCallee = AddBatchToListIndex(CallsByCallee, calls, c => c.CalleeSymbol);
        var callsByFile = AddBatchToListIndex(CallsByFile, calls, c => c.CallSiteFilePath);

        return this with {
            CallEdges = callEdges,
            CallsByCaller = callsByCaller,
            CallsByCallee = callsByCallee,
            CallsByFile = callsByFile,
        };
    }

    internal IndexSnapshot InsertDependencyEdges(IReadOnlyList<DependencyEdge> deps) {
        if (deps.Count == 0) return this;

        var depEdges = DepEdges.AddRange(deps);
        var depsBySource = AddBatchToListIndex(DepsBySource, deps, d => d.SourceSymbol);
        var depsByTarget = AddBatchToListIndex(DepsByTarget, deps, d => d.TargetSymbol);
        var depsByFile = AddBatchToListIndex(DepsByFile, deps.Where(d => !string.IsNullOrEmpty(d.SourceFilePath)), d => d.SourceFilePath!);

        return this with {
            DepEdges = depEdges,
            DepsBySource = depsBySource,
            DepsByTarget = depsByTarget,
            DepsByFile = depsByFile,
        };
    }

    /// <summary>修正 Inherits→Implements：当 target 是接口时替换边</summary>
    internal IndexSnapshot CorrectInheritsToImplements() {
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

    internal IndexSnapshot UpsertFileTracking(string filePath, string hash, int symbolCount, DateTimeOffset now, bool rebuildSorted = true) {
        var ft = FileTracking.SetItem(filePath, new FileTrackingEntry {
            FilePath = filePath,
            Hash = hash,
            SymbolCount = symbolCount,
            LastModified = now
        });
        if (!rebuildSorted) {
            return this with { FileTracking = ft };
        }
        return this with { FileTracking = ft, FileTrackingKeysSorted = RebuildSortedKeys(ft) };
    }

    internal IndexSnapshot RemoveProjectData(string filePath) {
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

    private static ImmutableHamT<TKey, ImmutableList<T>> AddToListIndex<TKey, T>(
        ImmutableHamT<TKey, ImmutableList<T>> dict, TKey key, T item) where TKey : notnull {
        var list = dict.GetValueOrDefault(key) ?? ImmutableList<T>.Empty;
        return dict.SetItem(key, list.Add(item));
    }

    /// <summary>批量添加到列表索引 — 按 key 分组后一次性 AddRange，减少 ImmutableList 平衡树重建次数</summary>
    private static ImmutableHamT<TKey, ImmutableList<T>> AddBatchToListIndex<TKey, T>(
        ImmutableHamT<TKey, ImmutableList<T>> dict,
        IEnumerable<T> items,
        Func<T, TKey> keySelector) where TKey : notnull {
        foreach (var g in items.GroupBy(keySelector)) {
            var list = dict.GetValueOrDefault(g.Key) ?? ImmutableList<T>.Empty;
            dict = dict.SetItem(g.Key, list.AddRange(g));
        }
        return dict;
    }

    private static ImmutableHamT<TKey, ImmutableList<T>> RemoveFromListIndex<TKey, T>(
        ImmutableHamT<TKey, ImmutableList<T>> dict, TKey key, T item) where TKey : notnull {
        var list = dict.GetValueOrDefault(key);
        if (list is null) return dict;
        var newList = list.Remove(item);
        return newList.IsEmpty ? dict.Remove(key) : dict.SetItem(key, newList);
    }

    private static ImmutableHamT<TKey, ImmutableList<DependencyEdge>> ReplaceEdgesInLists<TKey>(
        ImmutableHamT<TKey, ImmutableList<DependencyEdge>> dict,
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

    private static ImmutableList<string> RebuildSortedKeys(ImmutableHamT<string, FileTrackingEntry> ft)
        => ft.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToImmutableList();

    private static ImmutableList<SymbolInfo> RebuildSymbolsSortedByFqn(ImmutableHamT<string, SymbolInfo> symbols)
        => symbols.Values.OrderBy(s => s.FullyQualifiedName, StringComparer.Ordinal).ToImmutableList();

    private static ImmutableList<SymbolInfo> RebuildSymbolsSortedByName(ImmutableHamT<string, ImmutableList<SymbolInfo>> symbolsByName)
        => symbolsByName.Values.SelectMany(v => v).OrderBy(s => s.Name, StringComparer.Ordinal).ToImmutableList();
}
