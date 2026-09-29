namespace JoinCode.CodeIndex.Tests;

/// <summary>
/// IndexSnapshot 索引中枢写操作确定性测试 — 验证 RemoveFileData/InsertSymbols/
/// CorrectInheritsToImplements/UpsertFileTracking/RemoveProjectData 的分支行为与索引同步
/// </summary>
public sealed class IndexSnapshotTests {
    // ============ 辅助构造 ============

    private static SymbolInfo Sym(string name, string fqn, SymbolKind kind, string file, string? ns = null) => new() {
        Name = name,
        FullyQualifiedName = fqn,
        Kind = kind,
        FilePath = file,
        StartLine = 1,
        EndLine = 10,
        StartColumn = 1,
        EndColumn = 20,
        Namespace = ns,
    };

    private static CallEdge Edge(string caller, string callee, string file, int line = 1, CallKind kind = CallKind.Direct) => new() {
        CallerSymbol = caller,
        CalleeSymbol = callee,
        CallSiteFilePath = file,
        CallSiteLine = line,
        CallKind = kind,
    };

    private static DependencyEdge Dep(string src, string tgt, DependencyKind kind, string? file = null) => new() {
        SourceSymbol = src,
        TargetSymbol = tgt,
        DependencyKind = kind,
        SourceFilePath = file,
    };

    // ============ RemoveFileData ============

    [Fact]
    public void RemoveFileData_RemovesSymbolsFromAllFourIndexes() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([
            Sym("Foo", "Ns.Foo", SymbolKind.Class, "a.cs", "Ns"),
            Sym("Bar", "Ns.Bar", SymbolKind.Method, "a.cs", "Ns"),
            Sym("Baz", "Ns.Baz", SymbolKind.Class, "b.cs", "Ns"),
        ]);

        var after = snap.RemoveFileData("a.cs");

        // a.cs 的符号被移除,b.cs 保留
        Assert.False(after.SymbolsByFqn.ContainsKey("Ns.Foo"));
        Assert.False(after.SymbolsByFqn.ContainsKey("Ns.Bar"));
        Assert.True(after.SymbolsByFqn.ContainsKey("Ns.Baz"));
        Assert.False(after.SymbolsByFile.ContainsKey("a.cs"));
        Assert.True(after.SymbolsByFile.ContainsKey("b.cs"));
        // ByName 索引同步
        Assert.False(after.SymbolsByName.ContainsKey("Foo"));
        Assert.False(after.SymbolsByName.ContainsKey("Bar"));
        Assert.True(after.SymbolsByName.ContainsKey("Baz"));
        // ByKind 索引同步
        var classes = after.SymbolsByKind.GetValueOrDefault(SymbolKind.Class) ?? ImmutableList<SymbolInfo>.Empty;
        Assert.Single(classes);
        Assert.Equal("Ns.Baz", classes[0].FullyQualifiedName);
    }

    [Fact]
    public void RemoveFileData_RemovesCallEdgesFromAllFourIndexes() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([
            Sym("A", "Ns.A", SymbolKind.Method, "a.cs"),
            Sym("B", "Ns.B", SymbolKind.Method, "b.cs"),
            Sym("C", "Ns.C", SymbolKind.Method, "c.cs"),
        ]);
        snap = snap.InsertCallEdges([
            Edge("Ns.A", "Ns.B", "a.cs", 5),
            Edge("Ns.C", "Ns.B", "c.cs", 3),
        ]);

        var after = snap.RemoveFileData("a.cs");

        // a.cs 的调用边被移除
        Assert.Single(after.CallEdges);
        Assert.Equal("Ns.C", after.CallEdges[0].CallerSymbol);
        Assert.False(after.CallsByFile.ContainsKey("a.cs"));
        // CallsByCaller 同步:Ns.A 的出边被移除
        Assert.False(after.CallsByCaller.ContainsKey("Ns.A"));
        Assert.True(after.CallsByCaller.ContainsKey("Ns.C"));
        // CallsByCallee 同步:Ns.B 仍有一条入边(来自 C)
        var calleeB = after.CallsByCallee.GetValueOrDefault("Ns.B") ?? ImmutableList<CallEdge>.Empty;
        Assert.Single(calleeB);
    }

    [Fact]
    public void RemoveFileData_RemovesDepEdgesFromAllFourIndexes() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertDependencyEdges([
            Dep("Ns.A", "Ns.B", DependencyKind.Inherits, "a.cs"),
            Dep("Ns.C", "Ns.B", DependencyKind.Uses, "c.cs"),
        ]);

        var after = snap.RemoveFileData("a.cs");

        Assert.Single(after.DepEdges);
        Assert.Equal(DependencyKind.Uses, after.DepEdges[0].DependencyKind);
        Assert.False(after.DepsByFile.ContainsKey("a.cs"));
        Assert.False(after.DepsBySource.ContainsKey("Ns.A"));
        Assert.True(after.DepsBySource.ContainsKey("Ns.C"));
    }

    [Fact]
    public void RemoveFileData_NoMatch_ReturnsSameSnapshot() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([Sym("Foo", "Ns.Foo", SymbolKind.Class, "a.cs")]);

        var after = snap.RemoveFileData("nonexistent.cs");

        Assert.True(after.SymbolsByFqn.ContainsKey("Ns.Foo"));
    }

    [Fact]
    public void RemoveFileData_RebuildSortedTrue_UpdatesSortedIndexes() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([
            Sym("Zoo", "Ns.Zoo", SymbolKind.Class, "a.cs"),
            Sym("Apple", "Ns.Apple", SymbolKind.Class, "b.cs"),
        ]);

        var after = snap.RemoveFileData("a.cs", rebuildSorted: true);

        // SymbolsSortedByFqn 应只含 Apple 且已排序
        Assert.Single(after.SymbolsSortedByFqn);
        Assert.Equal("Ns.Apple", after.SymbolsSortedByFqn[0].FullyQualifiedName);
    }

    [Fact]
    public void RemoveFileData_RebuildSortedFalse_SkipsSortedRebuild() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([
            Sym("Zoo", "Ns.Zoo", SymbolKind.Class, "a.cs"),
            Sym("Apple", "Ns.Apple", SymbolKind.Class, "b.cs"),
        ]);

        var after = snap.RemoveFileData("a.cs", rebuildSorted: false);

        // 符号本身被移除
        Assert.False(after.SymbolsByFqn.ContainsKey("Ns.Zoo"));
        // 但排序列表未重建(仍含原两个符号)
        Assert.Equal(2, after.SymbolsSortedByFqn.Count);
    }

    // ============ InsertSymbols ============

    [Fact]
    public void InsertSymbols_EmptyList_ReturnsSameSnapshot() {
        var snap = IndexSnapshot.Empty;
        var after = snap.InsertSymbols([]);
        Assert.Same(snap, after);
    }

    [Fact]
    public void InsertSymbols_IsRebuildTrue_ReplacesExistingByFqn() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([Sym("Foo", "Ns.Foo", SymbolKind.Class, "a.cs")]);

        // 同 FQN 不同 Name/File — isRebuild=true 应先移除旧条目再插入新
        var after = snap.InsertSymbols([Sym("Foo2", "Ns.Foo", SymbolKind.Method, "b.cs")], isRebuild: true);

        var sym = after.SymbolsByFqn["Ns.Foo"];
        Assert.Equal("Foo2", sym.Name);
        Assert.Equal("b.cs", sym.FilePath);
        Assert.Equal(SymbolKind.Method, sym.Kind);
        // 旧 Name 索引被清理
        Assert.False(after.SymbolsByName.ContainsKey("Foo"));
        Assert.True(after.SymbolsByName.ContainsKey("Foo2"));
        // 旧 File 索引被清理
        Assert.False(after.SymbolsByFile.ContainsKey("a.cs"));
        Assert.True(after.SymbolsByFile.ContainsKey("b.cs"));
    }

    [Fact]
    public void InsertSymbols_IsRebuildFalse_BatchAddWithoutRemovingOld() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([Sym("Foo", "Ns.Foo", SymbolKind.Class, "a.cs")]);

        // isRebuild=false: 不检查 FQN 冲突,直接批量添加
        var after = snap.InsertSymbols([
            Sym("Bar", "Ns.Bar", SymbolKind.Method, "b.cs"),
        ], rebuildSorted: true, isRebuild: false);

        Assert.True(after.SymbolsByFqn.ContainsKey("Ns.Foo"));
        Assert.True(after.SymbolsByFqn.ContainsKey("Ns.Bar"));
        // ByName 批量添加
        Assert.True(after.SymbolsByName.ContainsKey("Bar"));
    }

    [Fact]
    public void InsertSymbols_RebuildSortedFalse_SkipsSortedRebuild() {
        var snap = IndexSnapshot.Empty;
        var after = snap.InsertSymbols([Sym("Foo", "Ns.Foo", SymbolKind.Class, "a.cs")], rebuildSorted: false);

        Assert.True(after.SymbolsByFqn.ContainsKey("Ns.Foo"));
        // 排序列表未重建(仍为空)
        Assert.Empty(after.SymbolsSortedByFqn);
    }

    [Fact]
    public void InsertSymbols_MaintainsAllFourIndexes() {
        var snap = IndexSnapshot.Empty;
        var after = snap.InsertSymbols([
            Sym("Foo", "Ns.Foo", SymbolKind.Class, "a.cs", "Ns"),
            Sym("Bar", "Ns.Bar", SymbolKind.Method, "a.cs", "Ns"),
        ]);

        // ByFqn
        Assert.Equal(2, after.SymbolsByFqn.Count);
        // ByName
        Assert.True(after.SymbolsByName.ContainsKey("Foo"));
        Assert.True(after.SymbolsByName.ContainsKey("Bar"));
        // ByFile — 同文件两个符号
        var byFile = after.SymbolsByFile["a.cs"];
        Assert.Equal(2, byFile.Count);
        // ByKind
        Assert.True(after.SymbolsByKind.ContainsKey(SymbolKind.Class));
        Assert.True(after.SymbolsByKind.ContainsKey(SymbolKind.Method));
    }

    // ============ CorrectInheritsToImplements ============

    [Fact]
    public void CorrectInheritsToImplements_InterfaceTarget_ChangesToImplements() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([
            Sym("IFoo", "Ns.IFoo", SymbolKind.Interface, "i.cs"),
            Sym("Foo", "Ns.Foo", SymbolKind.Class, "f.cs"),
        ]);
        snap = snap.InsertDependencyEdges([Dep("Ns.Foo", "Ns.IFoo", DependencyKind.Inherits, "f.cs")]);

        var after = snap.CorrectInheritsToImplements();

        Assert.Single(after.DepEdges);
        Assert.Equal(DependencyKind.Implements, after.DepEdges[0].DependencyKind);
        // DepsBySource 同步
        var src = after.DepsBySource["Ns.Foo"];
        Assert.Single(src);
        Assert.Equal(DependencyKind.Implements, src[0].DependencyKind);
    }

    [Fact]
    public void CorrectInheritsToImplements_NonInterfaceTarget_NoChange() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([
            Sym("Base", "Ns.Base", SymbolKind.Class, "b.cs"),
            Sym("Derived", "Ns.Derived", SymbolKind.Class, "d.cs"),
        ]);
        snap = snap.InsertDependencyEdges([Dep("Ns.Derived", "Ns.Base", DependencyKind.Inherits, "d.cs")]);

        var after = snap.CorrectInheritsToImplements();

        Assert.Equal(DependencyKind.Inherits, after.DepEdges[0].DependencyKind);
    }

    [Fact]
    public void CorrectInheritsToImplements_AlreadyImplements_NoChange() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([Sym("IFoo", "Ns.IFoo", SymbolKind.Interface, "i.cs")]);
        snap = snap.InsertDependencyEdges([Dep("Ns.Foo", "Ns.IFoo", DependencyKind.Implements, "f.cs")]);

        var after = snap.CorrectInheritsToImplements();

        Assert.Equal(DependencyKind.Implements, after.DepEdges[0].DependencyKind);
    }

    [Fact]
    public void CorrectInheritsToImplements_NoInheritsEdges_ReturnsSameSnapshot() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([Sym("A", "Ns.A", SymbolKind.Class, "a.cs")]);
        snap = snap.InsertDependencyEdges([Dep("Ns.A", "Ns.B", DependencyKind.Uses, "a.cs")]);

        var after = snap.CorrectInheritsToImplements();

        Assert.Same(snap, after);
    }

    [Fact]
    public void CorrectInheritsToImplements_BatchCorrectsMultipleInherits() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([
            Sym("IA", "Ns.IA", SymbolKind.Interface, "ia.cs"),
            Sym("IB", "Ns.IB", SymbolKind.Interface, "ib.cs"),
        ]);
        snap = snap.InsertDependencyEdges([
            Dep("Ns.Foo", "Ns.IA", DependencyKind.Inherits, "f.cs"),
            Dep("Ns.Foo", "Ns.IB", DependencyKind.Inherits, "f.cs"),
            Dep("Ns.Bar", "Ns.IA", DependencyKind.Inherits, "b.cs"),
        ]);

        var after = snap.CorrectInheritsToImplements();

        Assert.Equal(3, after.DepEdges.Count);
        Assert.All(after.DepEdges, e => Assert.Equal(DependencyKind.Implements, e.DependencyKind));
    }

    // ============ UpsertFileTracking ============

    [Fact]
    public void UpsertFileTracking_AddsNewEntry() {
        var snap = IndexSnapshot.Empty;
        var now = DateTimeOffset.UtcNow;

        var after = snap.UpsertFileTracking("a.cs", "hash123", 5, now);

        Assert.True(after.FileTracking.ContainsKey("a.cs"));
        var entry = after.FileTracking["a.cs"];
        Assert.Equal("hash123", entry.Hash);
        Assert.Equal(5, entry.SymbolCount);
        Assert.Equal(now, entry.LastModified);
    }

    [Fact]
    public void UpsertFileTracking_UpdatesExistingEntry() {
        var snap = IndexSnapshot.Empty;
        var t1 = DateTimeOffset.UtcNow;
        snap = snap.UpsertFileTracking("a.cs", "hash1", 3, t1);
        var t2 = t1.AddMinutes(5);

        var after = snap.UpsertFileTracking("a.cs", "hash2", 7, t2);

        var entry = after.FileTracking["a.cs"];
        Assert.Equal("hash2", entry.Hash);
        Assert.Equal(7, entry.SymbolCount);
        Assert.Equal(t2, entry.LastModified);
    }

    [Fact]
    public void UpsertFileTracking_RebuildSortedTrue_UpdatesSortedKeys() {
        var snap = IndexSnapshot.Empty;
        snap = snap.UpsertFileTracking("z.cs", "h1", 1, DateTimeOffset.UtcNow);

        var after = snap.UpsertFileTracking("a.cs", "h2", 2, DateTimeOffset.UtcNow, rebuildSorted: true);

        // 排序列表应包含两个键且有序
        Assert.Equal(2, after.FileTrackingKeysSorted.Count);
        Assert.Equal("a.cs", after.FileTrackingKeysSorted[0]);
        Assert.Equal("z.cs", after.FileTrackingKeysSorted[1]);
    }

    [Fact]
    public void UpsertFileTracking_RebuildSortedFalse_SkipsSortedRebuild() {
        var snap = IndexSnapshot.Empty;
        snap = snap.UpsertFileTracking("z.cs", "h1", 1, DateTimeOffset.UtcNow);

        var after = snap.UpsertFileTracking("a.cs", "h2", 2, DateTimeOffset.UtcNow, rebuildSorted: false);

        Assert.True(after.FileTracking.ContainsKey("a.cs"));
        // 排序列表未重建(仍只含 z.cs)
        Assert.Single(after.FileTrackingKeysSorted);
    }

    // ============ RemoveProjectData ============

    [Fact]
    public void RemoveProjectData_RemovesProjectAndRefs() {
        var snap = IndexSnapshot.Empty;
        var projPath = "src/Lib/Lib.csproj";
        var targetPath = "src/Core/Core.csproj";
        snap = snap.IndexProject(projPath,
            new ProjectInfo { Name = "Lib", FilePath = projPath },
            [new ProjectReferenceEdge { SourceProjectPath = projPath, TargetProjectPath = targetPath }],
            [new NuGetPackageReference { ProjectPath = projPath, PackageName = "Newtonsoft.Json", Version = "13.0.3" }]);

        var after = snap.RemoveProjectData(projPath);

        Assert.False(after.Projects.ContainsKey(projPath));
        Assert.False(after.ProjectRefs.ContainsKey(projPath));
        Assert.False(after.NuGetRefs.ContainsKey(projPath));
    }

    [Fact]
    public void RemoveProjectData_RemovesReverseIndexEntries() {
        var snap = IndexSnapshot.Empty;
        var projPath = "src/Lib/Lib.csproj";
        var targetPath = "src/Core/Core.csproj";
        snap = snap.IndexProject(projPath,
            new ProjectInfo { Name = "Lib", FilePath = projPath },
            [new ProjectReferenceEdge { SourceProjectPath = projPath, TargetProjectPath = targetPath }],
            [new NuGetPackageReference { ProjectPath = projPath, PackageName = "Xunit", Version = "2.9.0" }]);

        var after = snap.RemoveProjectData(projPath);

        // 反向索引:ProjectRefsByTarget 应不再包含该边
        var normTarget = InMemoryIndexStore.NormalizeKey(targetPath);
        var refs = after.ProjectRefsByTarget.GetValueOrDefault(normTarget) ?? ImmutableList<ProjectReferenceEdge>.Empty;
        Assert.Empty(refs);
        // NuGetRefsByPackage 应不再包含 Xunit
        var pkgs = after.NuGetRefsByPackage.GetValueOrDefault("Xunit") ?? ImmutableList<NuGetPackageReference>.Empty;
        Assert.Empty(pkgs);
    }

    [Fact]
    public void RemoveProjectData_NoMatch_ReturnsSameSnapshot() {
        var snap = IndexSnapshot.Empty;
        snap = snap.IndexProject("a.csproj",
            new ProjectInfo { Name = "A", FilePath = "a.csproj" }, [], []);

        var after = snap.RemoveProjectData("nonexistent.csproj");

        Assert.True(after.Projects.ContainsKey("a.csproj"));
    }

    // ============ RemoveSymbolsOfFile (拆分子方法确定性测试) ============

    [Fact]
    public void RemoveSymbolsOfFile_RemovesAllSymbolsFromFourIndexes() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([
            Sym("Foo", "Ns.Foo", SymbolKind.Class, "a.cs", "Ns"),
            Sym("Bar", "Ns.Bar", SymbolKind.Method, "a.cs", "Ns"),
            Sym("Baz", "Ns.Baz", SymbolKind.Class, "b.cs", "Ns"),
        ]);

        var (byFqn, byName, byFile, byKind) = snap.RemoveSymbolsOfFile("a.cs");

        // a.cs 的两个符号从 ByFqn 移除,b.cs 保留
        Assert.False(byFqn.ContainsKey("Ns.Foo"));
        Assert.False(byFqn.ContainsKey("Ns.Bar"));
        Assert.True(byFqn.ContainsKey("Ns.Baz"));
        // ByFile 移除 a.cs 键
        Assert.False(byFile.ContainsKey("a.cs"));
        Assert.True(byFile.ContainsKey("b.cs"));
        // ByName 同步移除
        Assert.False(byName.ContainsKey("Foo"));
        Assert.False(byName.ContainsKey("Bar"));
        Assert.True(byName.ContainsKey("Baz"));
        // ByKind 同步:Class 只剩 Baz
        var classes = byKind.GetValueOrDefault(SymbolKind.Class) ?? ImmutableList<SymbolInfo>.Empty;
        Assert.Single(classes);
        Assert.Equal("Ns.Baz", classes[0].FullyQualifiedName);
        // Method 种类列表被清空(键移除)
        Assert.False(byKind.ContainsKey(SymbolKind.Method));
    }

    [Fact]
    public void RemoveSymbolsOfFile_NoMatch_ReturnsOriginalIndexes() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([Sym("Foo", "Ns.Foo", SymbolKind.Class, "a.cs")]);

        var (byFqn, byName, byFile, byKind) = snap.RemoveSymbolsOfFile("nonexistent.cs");

        // 全部保持原样
        Assert.True(byFqn.ContainsKey("Ns.Foo"));
        Assert.True(byFile.ContainsKey("a.cs"));
        Assert.True(byName.ContainsKey("Foo"));
        Assert.True(byKind.ContainsKey(SymbolKind.Class));
        Assert.Same(snap.SymbolsByFqn, byFqn);
    }

    [Fact]
    public void RemoveSymbolsOfFile_EmptySnapshot_ReturnsEmptyIndexes() {
        var snap = IndexSnapshot.Empty;

        var (byFqn, byName, byFile, byKind) = snap.RemoveSymbolsOfFile("any.cs");

        Assert.Empty(byFqn);
        Assert.Empty(byFile);
        Assert.Empty(byName);
        Assert.Empty(byKind);
    }

    [Fact]
    public void RemoveSymbolsOfFile_PreservesOtherFilesSymbols() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([
            Sym("A1", "Ns.A1", SymbolKind.Class, "a.cs"),
            Sym("A2", "Ns.A2", SymbolKind.Method, "a.cs"),
            Sym("B1", "Ns.B1", SymbolKind.Class, "b.cs"),
            Sym("C1", "Ns.C1", SymbolKind.Interface, "c.cs"),
        ]);

        var (byFqn, _, byFile, _) = snap.RemoveSymbolsOfFile("a.cs");

        // b.cs 和 c.cs 的符号完整保留
        Assert.True(byFqn.ContainsKey("Ns.B1"));
        Assert.True(byFqn.ContainsKey("Ns.C1"));
        Assert.True(byFile.ContainsKey("b.cs"));
        Assert.True(byFile.ContainsKey("c.cs"));
        Assert.Equal(2, byFqn.Count);
    }

    // ============ RemoveCallEdgesOfFile (拆分子方法确定性测试) ============

    [Fact]
    public void RemoveCallEdgesOfFile_RemovesEdgesFromFourIndexes() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertCallEdges([
            Edge("Ns.A", "Ns.B", "a.cs", 5),
            Edge("Ns.C", "Ns.B", "c.cs", 3),
        ]);

        var (edges, byCaller, byCallee, byFile) = snap.RemoveCallEdgesOfFile("a.cs");

        // a.cs 的边被移除,只剩 c.cs 的边
        Assert.Single(edges);
        Assert.Equal("Ns.C", edges[0].CallerSymbol);
        Assert.False(byFile.ContainsKey("a.cs"));
        Assert.True(byFile.ContainsKey("c.cs"));
        // CallsByCaller: Ns.A 出边被移除,Ns.C 保留
        Assert.False(byCaller.ContainsKey("Ns.A"));
        Assert.True(byCaller.ContainsKey("Ns.C"));
        // CallsByCallee: Ns.B 仍有一条入边(来自 C)
        var calleeB = byCallee.GetValueOrDefault("Ns.B") ?? ImmutableList<CallEdge>.Empty;
        Assert.Single(calleeB);
        Assert.Equal("Ns.C", calleeB[0].CallerSymbol);
    }

    [Fact]
    public void RemoveCallEdgesOfFile_NoMatch_ReturnsOriginalIndexes() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertCallEdges([Edge("A", "B", "a.cs", 1)]);

        var (edges, byCaller, byCallee, byFile) = snap.RemoveCallEdgesOfFile("nonexistent.cs");

        Assert.Single(edges);
        Assert.True(byFile.ContainsKey("a.cs"));
        Assert.Same(snap.CallEdges, edges);
    }

    [Fact]
    public void RemoveCallEdgesOfFile_EmptySnapshot_ReturnsEmptyIndexes() {
        var snap = IndexSnapshot.Empty;

        var (edges, byCaller, byCallee, byFile) = snap.RemoveCallEdgesOfFile("any.cs");

        Assert.Empty(edges);
        Assert.Empty(byCaller);
        Assert.Empty(byCallee);
        Assert.Empty(byFile);
    }

    [Fact]
    public void RemoveCallEdgesOfFile_PreservesOtherFilesEdges() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertCallEdges([
            Edge("A", "B", "a.cs", 1),
            Edge("C", "D", "c.cs", 2),
            Edge("E", "F", "e.cs", 3),
        ]);

        var (edges, _, _, byFile) = snap.RemoveCallEdgesOfFile("c.cs");

        // a.cs 和 e.cs 的边保留
        Assert.Equal(2, edges.Count);
        Assert.True(byFile.ContainsKey("a.cs"));
        Assert.True(byFile.ContainsKey("e.cs"));
        Assert.False(byFile.ContainsKey("c.cs"));
    }

    // ============ RemoveDepEdgesOfFile (拆分子方法确定性测试) ============

    [Fact]
    public void RemoveDepEdgesOfFile_RemovesEdgesFromFourIndexes() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertDependencyEdges([
            Dep("Ns.A", "Ns.B", DependencyKind.Inherits, "a.cs"),
            Dep("Ns.C", "Ns.B", DependencyKind.Uses, "c.cs"),
        ]);

        var (edges, bySource, byTarget, byFile) = snap.RemoveDepEdgesOfFile("a.cs");

        // a.cs 的依赖边被移除,只剩 c.cs 的边
        Assert.Single(edges);
        Assert.Equal(DependencyKind.Uses, edges[0].DependencyKind);
        Assert.False(byFile.ContainsKey("a.cs"));
        Assert.True(byFile.ContainsKey("c.cs"));
        // BySource: Ns.A 被移除,Ns.C 保留
        Assert.False(bySource.ContainsKey("Ns.A"));
        Assert.True(bySource.ContainsKey("Ns.C"));
        // ByTarget: Ns.B 仍有一条入边(来自 C)
        var targetB = byTarget.GetValueOrDefault("Ns.B") ?? ImmutableList<DependencyEdge>.Empty;
        Assert.Single(targetB);
        Assert.Equal("Ns.C", targetB[0].SourceSymbol);
    }

    [Fact]
    public void RemoveDepEdgesOfFile_NoMatch_ReturnsOriginalIndexes() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertDependencyEdges([Dep("A", "B", DependencyKind.Inherits, "a.cs")]);

        var (edges, bySource, byTarget, byFile) = snap.RemoveDepEdgesOfFile("nonexistent.cs");

        Assert.Single(edges);
        Assert.True(byFile.ContainsKey("a.cs"));
        Assert.Same(snap.DepEdges, edges);
    }

    [Fact]
    public void RemoveDepEdgesOfFile_EmptySnapshot_ReturnsEmptyIndexes() {
        var snap = IndexSnapshot.Empty;

        var (edges, bySource, byTarget, byFile) = snap.RemoveDepEdgesOfFile("any.cs");

        Assert.Empty(edges);
        Assert.Empty(bySource);
        Assert.Empty(byTarget);
        Assert.Empty(byFile);
    }

    [Fact]
    public void RemoveDepEdgesOfFile_PreservesOtherFilesEdges() {
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertDependencyEdges([
            Dep("A", "B", DependencyKind.Inherits, "a.cs"),
            Dep("C", "D", DependencyKind.Uses, "c.cs"),
        ]);

        var (edges, _, _, byFile) = snap.RemoveDepEdgesOfFile("a.cs");

        Assert.Single(edges);
        Assert.Equal("C", edges[0].SourceSymbol);
        Assert.True(byFile.ContainsKey("c.cs"));
        Assert.False(byFile.ContainsKey("a.cs"));
    }

    // ============ RemoveFileData 拆分后编排一致性 ============

    [Fact]
    public void RemoveFileData_Orchestration_MatchesThreeSubRemovals() {
        // 验证主方法编排 = 三段子方法独立执行的组合
        var snap = IndexSnapshot.Empty;
        snap = snap.InsertSymbols([
            Sym("Foo", "Ns.Foo", SymbolKind.Class, "a.cs"),
            Sym("Bar", "Ns.Bar", SymbolKind.Method, "a.cs"),
        ]);
        snap = snap.InsertCallEdges([Edge("Ns.Foo", "Ns.Bar", "a.cs", 5)]);
        snap = snap.InsertDependencyEdges([Dep("Ns.Foo", "Ns.Bar", DependencyKind.Inherits, "a.cs")]);

        var byMain = snap.RemoveFileData("a.cs");

        // 子方法独立执行
        var (symByFqn, _, _, _) = snap.RemoveSymbolsOfFile("a.cs");
        var (callEdges, _, _, _) = snap.RemoveCallEdgesOfFile("a.cs");
        var (depEdges, _, _, _) = snap.RemoveDepEdgesOfFile("a.cs");

        // 主方法结果应与子方法一致
        Assert.Equal(byMain.SymbolsByFqn, symByFqn);
        Assert.Equal(byMain.CallEdges, callEdges);
        Assert.Equal(byMain.DepEdges, depEdges);
    }
}
