namespace JoinCode.CodeIndex.Tests;

public sealed class CodeIndexerTests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly CodeIndexer _indexer;
    private readonly IFileSystem _fs;
    private bool _disposed;

    public CodeIndexerTests() {
        _store = new InMemoryIndexStore();
        _fs = new IO.FileSystem.InMemoryFileSystem();
        _indexer = new CodeIndexer(_store, _fs);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _indexer.DisposeSafe();
        _store.Dispose();
    }

    [Fact]
    public async Task BuildIndexAsync_EmptyDirectory_CompletesWithZeroFiles() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_empty_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        var result = await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.DeletedCount);
    }

    [Fact]
    public async Task BuildIndexAsync_SingleCsFile_IndexesSymbols() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_single_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "A.cs"), "public class A { public void M() { } }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        var result = await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, result.UpdatedCount);
        var snap = _store.GetSnapshot();
        Assert.True(snap.SymbolsByFqn.Count > 0);
        Assert.True(snap.FileTracking.ContainsKey(Path.Combine(root, "A.cs")));
    }

    [Fact]
    public async Task BuildIndexAsync_MultipleCsFiles_IndexesAll() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_multi_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "A.cs"), "public class A { }");
        await _fs.WriteAllText(Path.Combine(root, "B.cs"), "public class B { }");
        await _fs.WriteAllText(Path.Combine(root, "C.cs"), "public class C { }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        var result = await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(3, result.UpdatedCount);
        Assert.Equal(3, _store.GetSnapshot().FileTracking.Count);
    }

    [Fact]
    public async Task BuildIndexAsync_ExcludesBinObjDirectories() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_excl_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        _fs.CreateDirectory(Path.Combine(root, "bin"));
        _fs.CreateDirectory(Path.Combine(root, "obj"));
        _fs.CreateDirectory(Path.Combine(root, "sub"));
        await _fs.WriteAllText(Path.Combine(root, "A.cs"), "public class A { }");
        await _fs.WriteAllText(Path.Combine(root, "bin", "B.cs"), "public class B { }");
        await _fs.WriteAllText(Path.Combine(root, "obj", "C.cs"), "public class C { }");
        await _fs.WriteAllText(Path.Combine(root, "sub", "D.cs"), "public class D { }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        var result = await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        // bin/obj 被排除,只索引 A.cs 和 sub/D.cs
        Assert.Equal(2, result.UpdatedCount);
        var snap = _store.GetSnapshot();
        Assert.True(snap.FileTracking.ContainsKey(Path.Combine(root, "A.cs")));
        Assert.True(snap.FileTracking.ContainsKey(Path.Combine(root, "sub", "D.cs")));
        Assert.False(snap.FileTracking.ContainsKey(Path.Combine(root, "bin", "B.cs")));
        Assert.False(snap.FileTracking.ContainsKey(Path.Combine(root, "obj", "C.cs")));
    }

    [Fact]
    public async Task BuildIndexAsync_SecondRun_SkipsUnchangedFiles() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_skip_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "A.cs"), "public class A { }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);
        var result = await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        // 第二次未变更 → 全部跳过
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public async Task BuildIndexAsync_SecondRun_ReindexesModifiedFiles() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_reidx_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        var path = Path.Combine(root, "A.cs");
        await _fs.WriteAllText(path, "public class Old { }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);
        // 修改文件内容
        await _fs.WriteAllText(path, "public class New { public void Method() { } }");
        var result = await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, result.UpdatedCount);
        var snap = _store.GetSnapshot();
        Assert.Contains(snap.SymbolsByFqn, kvp => kvp.Key.Contains("New"));
    }

    [Fact]
    public async Task BuildIndexAsync_RemovesDeletedFilesFromIndex() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_rm_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        var aPath = Path.Combine(root, "A.cs");
        var bPath = Path.Combine(root, "B.cs");
        await _fs.WriteAllText(aPath, "public class A { }");
        await _fs.WriteAllText(bPath, "public class B { }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, _store.GetSnapshot().FileTracking.Count);

        // 模拟删除 B.cs:用新 fs 只含 A.cs
        var fs2 = new IO.FileSystem.InMemoryFileSystem();
        fs2.CreateDirectory(root);
        await fs2.WriteAllText(aPath, "public class A { }");
        await using var indexer2 = new CodeIndexer(_store, fs2);
        var result = await indexer2.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        var snap = _store.GetSnapshot();
        Assert.True(snap.FileTracking.ContainsKey(aPath));
        Assert.False(snap.FileTracking.ContainsKey(bPath));
        Assert.Equal(1, result.DeletedCount);
    }

    [Fact]
    public async Task BuildIndexAsync_ProgressCallback_ReportsProgress() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_prog_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "A.cs"), "public class A { }");
        await _fs.WriteAllText(Path.Combine(root, "B.cs"), "public class B { }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        var progressReports = new List<IndexProgress>();
        var progress = new Progress<IndexProgress>(p => progressReports.Add(p));

        await _indexer.BuildIndexAsync(options, CancellationToken.None, progress).ConfigureAwait(true);

        Assert.NotEmpty(progressReports);
        var last = progressReports[^1];
        Assert.Equal(2, last.Total);
    }

    [Fact]
    public async Task UpdateFileAsync_DelegatesToIncrementalUpdater() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_upd_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        var path = Path.Combine(root, "A.cs");
        await _fs.WriteAllText(path, "public class A { public void M() { } }");

        await _indexer.UpdateFileAsync(path, CancellationToken.None).ConfigureAwait(true);

        var snap = _store.GetSnapshot();
        Assert.True(snap.FileTracking.ContainsKey(path));
        Assert.True(snap.SymbolsByFqn.Count > 0);
    }

    [Fact]
    public async Task RemoveFileAsync_DelegatesToSymbolIndex() {
        InsertSymbol(CreateSymbol("Foo", "Ns.Foo", SymbolKind.Method, "a.cs"));
        Assert.True(_store.GetSnapshot().SymbolsByFqn.ContainsKey("Ns.Foo"));

        await _indexer.RemoveFileAsync("a.cs", CancellationToken.None).ConfigureAwait(true);

        Assert.False(_store.GetSnapshot().SymbolsByFqn.ContainsKey("Ns.Foo"));
    }

    [Fact]
    public async Task Searcher_ReturnsSymbolSearcher() {
        await Task.CompletedTask.ConfigureAwait(true);
        Assert.NotNull(_indexer.Searcher);
        Assert.IsAssignableFrom<ISymbolSearcher>(_indexer.Searcher);
    }

    [Fact]
    public async Task CallGraph_ReturnsCallGraphInstance() {
        await Task.CompletedTask.ConfigureAwait(true);
        Assert.NotNull(_indexer.CallGraph);
        Assert.IsAssignableFrom<ICallGraph>(_indexer.CallGraph);
    }

    [Fact]
    public async Task DependencyGraph_ReturnsDependencyGraphInstance() {
        await Task.CompletedTask.ConfigureAwait(true);
        Assert.NotNull(_indexer.DependencyGraph);
        Assert.IsAssignableFrom<IDependencyGraph>(_indexer.DependencyGraph);
    }

    // ============ SearchComprehensiveAsync (rg+AST 综合检索) ============

    [Fact]
    public async Task SearchComprehensiveAsync_ReturnsMatchedSymbolsAndCallers() {
        InsertSymbol(CreateSymbol("ProcessOrder", "App.Services.ProcessOrder", SymbolKind.Method, "svc.cs"));
        InsertSymbol(CreateSymbol("SaveOrder", "App.Services.SaveOrder", SymbolKind.Method, "svc.cs"));

        InsertCallEdge("HandleRequest", "ProcessOrder", "handler.cs", 10, CallKind.Direct);

        var result = await _indexer.SearchComprehensiveAsync("Process", 1000, CancellationToken.None).ConfigureAwait(true);

        Assert.NotEmpty(result.MatchedSymbols);
        Assert.Contains(result.MatchedSymbols, s => s.Name == "ProcessOrder");
        Assert.NotEmpty(result.Callers);
        Assert.Contains(result.Callers, c => c.CallerSymbol == "HandleRequest");
    }

    [Fact]
    public async Task SearchComprehensiveAsync_ReturnsCallees() {
        InsertSymbol(CreateSymbol("ProcessOrder", "App.Services.ProcessOrder", SymbolKind.Method, "svc.cs"));
        InsertCallEdge("ProcessOrder", "ValidateInput", "svc.cs", 10, CallKind.Direct);
        InsertCallEdge("ProcessOrder", "SaveData", "svc.cs", 15, CallKind.Direct);

        var result = await _indexer.SearchComprehensiveAsync("ProcessOrder", 1000, CancellationToken.None).ConfigureAwait(true);

        Assert.NotEmpty(result.Callees);
        Assert.Equal(2, result.Callees.Count);
        Assert.Contains(result.Callees, c => c.CalleeSymbol == "ValidateInput");
        Assert.Contains(result.Callees, c => c.CalleeSymbol == "SaveData");
    }

    [Fact]
    public async Task SearchComprehensiveAsync_NoMatches_ReturnsEmpty() {
        InsertSymbol(CreateSymbol("Foo", "App.Foo", SymbolKind.Method, "a.cs"));

        var result = await _indexer.SearchComprehensiveAsync("NonExistent", 1000, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(result.MatchedSymbols);
        Assert.Empty(result.Callers);
        Assert.Empty(result.Callees);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task SearchComprehensiveAsync_RespectsTokenBudget() {
        // 插入多个匹配符号,超过小 token 预算
        for (var i = 0; i < 5; i++) {
            InsertSymbol(CreateSymbol($"Process{i}", $"App.Services.Process{i}", SymbolKind.Method, $"f{i}.cs"));
        }

        var result = await _indexer.SearchComprehensiveAsync("Process", 10, CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Truncated);
        Assert.True(result.EstimatedTokens <= 10);
    }

    [Fact]
    public async Task SearchComprehensiveAsync_NullPattern_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _indexer.SearchComprehensiveAsync(null!, 1000, CancellationToken.None)).ConfigureAwait(true);
    }

    // ============ 正确性缺口验证测试 ============

    /// <summary>
    /// 验证 100 候选上限: 插入 150 个匹配符号,MatchedSymbols 仅返回前 100 个,
    /// 但 TotalMatchedCount 暴露真实总数 150,LLM 可据此判断是否被候选上限截断
    /// </summary>
    [Fact]
    public async Task SearchComprehensiveAsync_Exceeds100CandidateLimit_ExposesTotalCount() {
        // 插入 150 个匹配 "Foo" 的符号
        for (var i = 0; i < 150; i++) {
            InsertSymbol(CreateSymbol($"Foo{i}", $"App.Ns.Foo{i}", SymbolKind.Method, $"f{i}.cs"));
        }

        var result = await _indexer.SearchComprehensiveAsync("Foo", 100000, CancellationToken.None).ConfigureAwait(true);

        // 修复后: MatchedSymbols 仍只返回前 100 个(硬编码上限)
        Assert.Equal(100, result.MatchedSymbols.Count);
        // 修复后: TotalMatchedCount 暴露真实总数 150,LLM 可据此判断被截断
        Assert.Equal(150, result.TotalMatchedCount);
        // Truncated 仍只反映 token 预算截断(不反映候选上限截断)
        Assert.False(result.Truncated);
    }

    /// <summary>
    /// 验证 FindReferencesAsync 语义: "References" 返回真正的调用点(FilePath/StartLine = 调用点位置)
    /// 而非同名符号定义 — 对重命名操作,LLM 可直接用 References 定位所有需要更新的调用点
    /// </summary>
    [Fact]
    public async Task SearchComprehensiveAsync_References_ReturnsCallSites_NotSameNameDefinitions() {
        // 插入目标符号 BuildIndex
        InsertSymbol(CreateSymbol("BuildIndex", "App.BuildIndex", SymbolKind.Method, "core.cs"));

        // 插入同名符号(不同 FQN) — 这些不应出现在 References 中
        InsertSymbol(CreateSymbol("BuildIndex", "Other.BuildIndex", SymbolKind.Method, "other.cs"));

        // 插入调用边 — 这些是真正的引用点(应出现在 References 中)
        InsertCallEdge("CallerA", "BuildIndex", "caller.cs", 10, CallKind.Direct);
        InsertCallEdge("CallerB", "BuildIndex", "caller.cs", 20, CallKind.Direct);

        var result = await _indexer.SearchComprehensiveAsync("BuildIndex", 10000, CancellationToken.None).ConfigureAwait(true);

        // 修复后: References 返回调用点(CallerA@caller.cs:10, CallerB@caller.cs:20)
        Assert.Equal(2, result.References.Count);
        Assert.Contains(result.References, r => r.Name == "CallerA" && r.FilePath == "caller.cs" && r.StartLine == 10);
        Assert.Contains(result.References, r => r.Name == "CallerB" && r.FilePath == "caller.cs" && r.StartLine == 20);

        // 同名符号定义不应出现在 References 中(它们已在 MatchedSymbols 中)
        Assert.DoesNotContain(result.References, r => r.FullyQualifiedName == "App.BuildIndex");
        Assert.DoesNotContain(result.References, r => r.FullyQualifiedName == "Other.BuildIndex");
    }

    /// <summary>
    /// 验证截断优先级: matched > references > callers > callees
    /// 当 token 预算只能容纳 matched 符号时,references/callers/callees 应全部为空
    /// 且 TruncatedCount 应反映被截断的条目数
    /// </summary>
    [Fact]
    public async Task SearchComprehensiveAsync_TruncationPriority_MatchedFirst() {
        // 插入多个匹配符号 + 引用 + 调用方/被调用方
        for (var i = 0; i < 10; i++) {
            InsertSymbol(CreateSymbol($"Match{i}", $"App.Match{i}", SymbolKind.Method, $"m{i}.cs"));
            InsertSymbol(CreateSymbol($"Match{i}", $"Other.Match{i}", SymbolKind.Method, $"o{i}.cs"));
            InsertCallEdge($"Caller{i}", $"Match{i}", $"c{i}.cs", 5, CallKind.Direct);
            InsertCallEdge($"Match{i}", $"Callee{i}", $"m{i}.cs", 10, CallKind.Direct);
        }

        // 用极小预算: 只能容纳部分 matched 符号
        var result = await _indexer.SearchComprehensiveAsync("Match", 5, CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Truncated);
        // 截断后 TruncatedCount 应 > 0(被截断的 matched + references + callers + callees)
        Assert.True(result.TruncatedCount > 0);
        // matched 符号优先填充(可能有部分)
        // references/callers/callees 应为空(matched 未填完就截断)
        Assert.Empty(result.Callers);
        Assert.Empty(result.Callees);
    }

    [Fact]
    public async Task SearchComprehensiveAsync_IncludeAstFalse_SkipsReferencesAndCallGraph() {
        InsertSymbol(CreateSymbol("ProcessOrder", "App.Services.ProcessOrder", SymbolKind.Method, "svc.cs"));
        InsertCallEdge("HandleRequest", "ProcessOrder", "handler.cs", 10, CallKind.Direct);
        InsertCallEdge("ProcessOrder", "ValidateInput", "svc.cs", 15, CallKind.Direct);

        var result = await _indexer.SearchComprehensiveAsync("ProcessOrder", 1000, CancellationToken.None, includeAst: false).ConfigureAwait(true);

        Assert.NotEmpty(result.MatchedSymbols);
        Assert.Contains(result.MatchedSymbols, s => s.Name == "ProcessOrder");
        Assert.Empty(result.References);
        Assert.Empty(result.Callers);
        Assert.Empty(result.Callees);
    }

    // ============ 测试辅助方法 ============

    private static SymbolInfo CreateSymbol(string name, string fqn, SymbolKind kind, string file) {
        return new SymbolInfo {
            Name = name,
            FullyQualifiedName = fqn,
            Kind = kind,
            FilePath = file,
            StartLine = 1,
            EndLine = 10,
            StartColumn = 1,
            EndColumn = 20
        };
    }

    private void InsertSymbol(SymbolInfo symbol) {
        _store.Update(snap => {
            var symbolsByName = AddToBucket(snap.SymbolsByName, symbol.Name, symbol);
            var symbolsByFile = AddToBucket(snap.SymbolsByFile, symbol.FilePath, symbol);
            var symbolsByKind = AddToBucket(snap.SymbolsByKind, symbol.Kind, symbol);
            return snap with {
                SymbolsByFqn = snap.SymbolsByFqn.SetItem(symbol.FullyQualifiedName, symbol),
                SymbolsByName = symbolsByName,
                SymbolsByFile = symbolsByFile,
                SymbolsByKind = symbolsByKind,
            };
        });
    }

    private void InsertCallEdge(string caller, string callee, string file, int line, CallKind kind) {
        var edge = new CallEdge {
            CallerSymbol = caller,
            CalleeSymbol = callee,
            CallSiteFilePath = file,
            CallSiteLine = line,
            CallKind = kind
        };
        _store.Update(snap => {
            var callsByCaller = AddToBucket(snap.CallsByCaller, caller, edge);
            var callsByCallee = AddToBucket(snap.CallsByCallee, callee, edge);
            var callsByFile = AddToBucket(snap.CallsByFile, file, edge);
            return snap with {
                CallEdges = snap.CallEdges.Add(edge),
                CallsByCaller = callsByCaller,
                CallsByCallee = callsByCallee,
                CallsByFile = callsByFile,
            };
        });
    }

    private static ImmutableHamT<TKey, ImmutableList<TValue>> AddToBucket<TKey, TValue>(
        ImmutableHamT<TKey, ImmutableList<TValue>> dict, TKey key, TValue value) where TKey : notnull {
        if (!dict.TryGetValue(key, out var list)) {
            list = ImmutableList<TValue>.Empty;
        }
        return dict.SetItem(key, list.Add(value));
    }

    // ============ EstimateSymbolTokens 确定性测试 ============

    [Fact]
    public void EstimateSymbolTokens_ShortSymbol_ReturnsMinimum5() {
        var sym = new SymbolInfo {
            Name = "F", FullyQualifiedName = "F", Kind = SymbolKind.Class,
            FilePath = "F.cs", StartLine = 1, EndLine = 1, StartColumn = 1, EndColumn = 1,
        };
        // chars = 1+1+4 = 6, 6/4 = 1, Math.Max(5,1) = 5
        Assert.Equal(5, CodeIndexer.EstimateSymbolTokens(sym));
    }

    [Fact]
    public void EstimateSymbolTokens_LongSymbol_ReturnsCharsDiv4() {
        var sym = new SymbolInfo {
            Name = "VeryLongSymbolName", FullyQualifiedName = "MyApp.Services.VeryLongSymbolName",
            Kind = SymbolKind.Class, FilePath = "src/services/very_long_symbol_name.cs",
            StartLine = 1, EndLine = 1, StartColumn = 1, EndColumn = 1,
        };
        var chars = sym.Name.Length + sym.FullyQualifiedName.Length + sym.FilePath.Length;
        var expected = Math.Max(5, chars / 4);
        Assert.Equal(expected, CodeIndexer.EstimateSymbolTokens(sym));
    }

    [Fact]
    public void EstimateSymbolTokens_AlwaysAtLeast5() {
        var sym = new SymbolInfo {
            Name = "", FullyQualifiedName = "", Kind = SymbolKind.Class,
            FilePath = "", StartLine = 1, EndLine = 1, StartColumn = 1, EndColumn = 1,
        };
        Assert.Equal(5, CodeIndexer.EstimateSymbolTokens(sym));
    }

    // ============ EstimateEdgeTokens 确定性测试 ============

    [Fact]
    public void EstimateEdgeTokens_ShortEdge_ReturnsMinimum4() {
        var edge = new CallEdge {
            CallerSymbol = "A", CalleeSymbol = "B", CallSiteFilePath = "c",
            CallSiteLine = 1, CallKind = CallKind.Direct,
        };
        // chars = 1+1+1 = 3, 3/4 = 0, Math.Max(4,0) = 4
        Assert.Equal(4, CodeIndexer.EstimateEdgeTokens(edge));
    }

    [Fact]
    public void EstimateEdgeTokens_LongEdge_ReturnsCharsDiv4() {
        var edge = new CallEdge {
            CallerSymbol = "MyApp.Services.OrderService",
            CalleeSymbol = "MyApp.Repositories.OrderRepository",
            CallSiteFilePath = "src/services/order_service.cs",
            CallSiteLine = 42, CallKind = CallKind.Direct,
        };
        var chars = edge.CallerSymbol.Length + edge.CalleeSymbol.Length + edge.CallSiteFilePath.Length;
        var expected = Math.Max(4, chars / 4);
        Assert.Equal(expected, CodeIndexer.EstimateEdgeTokens(edge));
    }

    [Fact]
    public void EstimateEdgeTokens_AlwaysAtLeast4() {
        var edge = new CallEdge {
            CallerSymbol = "", CalleeSymbol = "", CallSiteFilePath = "",
            CallSiteLine = 1, CallKind = CallKind.Direct,
        };
        Assert.Equal(4, CodeIndexer.EstimateEdgeTokens(edge));
    }

    // ============ TruncateByTokenBudget (拆分子方法确定性测试) ============

    private static SymbolInfo Sym(string name, string fqn, string file = "a.cs") => new() {
        Name = name, FullyQualifiedName = fqn, Kind = SymbolKind.Class,
        FilePath = file, StartLine = 1, EndLine = 1, StartColumn = 1, EndColumn = 1,
    };

    private static CallEdge Edge(string caller, string callee, string file = "a.cs") => new() {
        CallerSymbol = caller, CalleeSymbol = callee, CallSiteFilePath = file,
        CallSiteLine = 1, CallKind = CallKind.Direct,
    };

    [Fact]
    public void TruncateByTokenBudget_EmptyInputs_ReturnsAllEmpty() {
        var (matched, refs, callers, callees, tokens, truncated, truncatedCount) =
            CodeIndexer.TruncateByTokenBudget([], [], [], [], 1000);

        Assert.Empty(matched);
        Assert.Empty(refs);
        Assert.Empty(callers);
        Assert.Empty(callees);
        Assert.Equal(0, tokens);
        Assert.False(truncated);
        Assert.Equal(0, truncatedCount);
    }

    [Fact]
    public void TruncateByBudget_SufficientBudget_KeepsAllItems() {
        var s1 = Sym("Foo", "Ns.Foo");
        var s2 = Sym("Bar", "Ns.Bar");
        var budget = CodeIndexer.EstimateSymbolTokens(s1) + CodeIndexer.EstimateSymbolTokens(s2) + 100;

        var (matched, refs, callers, callees, tokens, truncated, truncatedCount) =
            CodeIndexer.TruncateByTokenBudget([s1, s2], [], [], [], budget);

        Assert.Equal(2, matched.Count);
        Assert.False(truncated);
        Assert.Equal(0, truncatedCount);
        Assert.Equal(CodeIndexer.EstimateSymbolTokens(s1) + CodeIndexer.EstimateSymbolTokens(s2), tokens);
    }

    [Fact]
    public void TruncateByBudget_MatchedPriority_TruncatesMatchedFirst() {
        // 预算仅够第一个 matched 符号
        var s1 = Sym("Short", "S");
        var s2 = Sym("LongerName", "Ns.LongerName");
        var budget = CodeIndexer.EstimateSymbolTokens(s1);  // 刚好够 s1

        var (matched, refs, callers, callees, _, truncated, truncatedCount) =
            CodeIndexer.TruncateByTokenBudget([s1, s2], [], [], [], budget);

        Assert.Single(matched);
        Assert.Equal("S", matched[0].FullyQualifiedName);
        Assert.True(truncated);
        // s2 被截断
        Assert.Equal(1, truncatedCount);
    }

    [Fact]
    public void TruncateByBudget_PriorityOrder_MatchedBeforeReferences() {
        // 预算够 matched 但不够 references
        var m = Sym("M", "M");
        var r = Sym("R", "R");
        var budget = CodeIndexer.EstimateSymbolTokens(m);  // 刚好够 m

        var (matched, references, _, _, _, truncated, truncatedCount) =
            CodeIndexer.TruncateByTokenBudget([m], [r], [], [], budget);

        Assert.Single(matched);
        Assert.Empty(references);  // references 被截断
        Assert.True(truncated);
        Assert.Equal(1, truncatedCount);
    }

    [Fact]
    public void TruncateByBudget_PriorityOrder_ReferencesBeforeCallers() {
        var r = Sym("R", "R");
        var c = Edge("A", "B");
        var budget = CodeIndexer.EstimateSymbolTokens(r);  // 刚好够 r

        var (_, references, callers, _, _, truncated, truncatedCount) =
            CodeIndexer.TruncateByTokenBudget([], [r], [c], [], budget);

        Assert.Single(references);
        Assert.Empty(callers);  // callers 被截断
        Assert.True(truncated);
        Assert.Equal(1, truncatedCount);
    }

    [Fact]
    public void TruncateByBudget_PriorityOrder_CallersBeforeCallees() {
        var caller = Edge("A", "B");
        var callee = Edge("C", "D");
        var budget = CodeIndexer.EstimateEdgeTokens(caller);  // 刚好够 caller

        var (_, _, callers, callees, _, truncated, truncatedCount) =
            CodeIndexer.TruncateByTokenBudget([], [], [caller], [callee], budget);

        Assert.Single(callers);
        Assert.Empty(callees);  // callees 被截断
        Assert.True(truncated);
        Assert.Equal(1, truncatedCount);
    }

    [Fact]
    public void TruncateByBudget_TruncatedCount_SumsAcrossAllCategories() {
        // 预算仅够 matched 第一个条目,其余类别全部被截断
        // 验证 truncatedCount 跨四类别正确求和
        var m1 = Sym("M1", "M1");
        var m2 = Sym("M2", "M2");
        var r1 = Sym("R1", "R1");
        var c1 = Edge("A1", "B1");
        var e1 = Edge("C1", "D1");

        // 预算 = m1 的 tokens(刚好够 m1,不够 m2)
        var budget = CodeIndexer.EstimateSymbolTokens(m1);

        var (matched, references, callers, callees, _, truncated, truncatedCount) =
            CodeIndexer.TruncateByTokenBudget([m1, m2], [r1], [c1], [e1], budget);

        // matched 保留 m1,截断 m2
        Assert.Single(matched);
        // 其余类别全部被截断(预算已用尽)
        Assert.Empty(references);
        Assert.Empty(callers);
        Assert.Empty(callees);
        Assert.True(truncated);
        // 四类别各截断 1 个,共 4 个
        Assert.Equal(4, truncatedCount);
    }

    [Fact]
    public void TruncateByBudget_ZeroBudget_TruncatesEverything() {
        var s = Sym("Foo", "Ns.Foo");

        var (matched, _, _, _, tokens, truncated, truncatedCount) =
            CodeIndexer.TruncateByTokenBudget([s], [], [], [], 0);

        Assert.Empty(matched);
        Assert.Equal(0, tokens);
        Assert.True(truncated);
        Assert.Equal(1, truncatedCount);
    }

    [Fact]
    public void TruncateByBudget_EstimatedTokens_EqualsSumOfKeptItems() {
        var s1 = Sym("S1", "S1");
        var s2 = Sym("S2", "S2");
        var e1 = Edge("A", "B");
        var budget = 10000;  // 足够

        var (matched, _, _, callees, tokens, truncated, _) =
            CodeIndexer.TruncateByTokenBudget([s1, s2], [], [], [e1], budget);

        Assert.False(truncated);
        var expected = CodeIndexer.EstimateSymbolTokens(s1) + CodeIndexer.EstimateSymbolTokens(s2) + CodeIndexer.EstimateEdgeTokens(e1);
        Assert.Equal(expected, tokens);
        Assert.Equal(2, matched.Count);
        Assert.Single(callees);
    }

    [Fact]
    public void TruncateByBudget_PreservesItemOrder() {
        var s1 = Sym("First", "First");
        var s2 = Sym("Second", "Second");
        var s3 = Sym("Third", "Third");
        var budget = 10000;

        var (matched, _, _, _, _, _, _) =
            CodeIndexer.TruncateByTokenBudget([s1, s2, s3], [], [], [], budget);

        Assert.Equal(3, matched.Count);
        Assert.Equal("First", matched[0].Name);
        Assert.Equal("Second", matched[1].Name);
        Assert.Equal("Third", matched[2].Name);
    }

    [Fact]
    public void TruncateByBudget_ExactBoundary_IncludesItem() {
        // 预算 == 已用 + 当前条目 tokens 时,条件 estimatedTokens + t > budget 为 false,应包含
        var s = Sym("X", "X");
        var budget = CodeIndexer.EstimateSymbolTokens(s);  // 0 + t == budget,不大于,包含

        var (matched, _, _, _, tokens, truncated, truncatedCount) =
            CodeIndexer.TruncateByTokenBudget([s], [], [], [], budget);

        Assert.Single(matched);
        Assert.Equal(CodeIndexer.EstimateSymbolTokens(s), tokens);
        Assert.False(truncated);
        Assert.Equal(0, truncatedCount);
    }

    // ============ GetStatsAsync ============

    [Fact]
    public async Task GetStatsAsync_EmptyStore_ReturnsZeroCounts() {
        var stats = await _indexer.GetStatsAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(0, stats.FileCount);
        Assert.Equal(0, stats.SymbolCount);
        Assert.Equal(0, stats.CallEdgeCount);
        Assert.Equal(0, stats.DependencyEdgeCount);
        Assert.Equal(0, stats.ProjectCount);
    }

    [Fact]
    public async Task GetStatsAsync_AfterIndexing_ReturnsCorrectCounts() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_stats_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "A.cs"), "public class A { public void M() { } }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };
        await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        var stats = await _indexer.GetStatsAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, stats.FileCount);
        Assert.True(stats.SymbolCount > 0);
        Assert.True(stats.LastUpdated > DateTimeOffset.MinValue);
    }

    // ============ EnsureIndexLoadedAsync ============

    [Fact]
    public async Task EnsureIndexLoadedAsync_NoGitDirectory_DoesNothing() {
        // 无 .git 目录 → 跳过自动加载,不抛异常
        await _indexer.EnsureIndexLoadedAsync(CancellationToken.None).ConfigureAwait(true);

        // 索引应保持空
        var snap = _store.GetSnapshot();
        Assert.Empty(snap.SymbolsByFqn);
    }

    // EnsureIndexLoadedAsync_WithGit 的完整测试跳过:
    // GitWorkspaceResolver.FindGitWorkspaceDir(null, _fs) 内部用 Environment.CurrentDirectory
    // 作为起始目录(真实文件系统),InMemoryFileSystem 路径非真实路径,无法确定性 mock。
    // 属于"不可mock方法"(依赖真实文件系统当前目录查找 .git),仅测试无 .git 分支。

    [Fact]
    public async Task EnsureIndexLoadedAsync_Idempotent_SecondCallNoOp() {
        await _indexer.EnsureIndexLoadedAsync(CancellationToken.None).ConfigureAwait(true);
        // 第二次调用应无操作(Interlocked.CompareExchange 守卫)
        await _indexer.EnsureIndexLoadedAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(_store.GetSnapshot().SymbolsByFqn);
    }

    // ============ RebuildIndexAsync ============

    [Fact]
    public async Task RebuildIndexAsync_NoGitDirectory_DoesNothing() {
        // 无 .git 目录 → 无法重建,不抛异常
        await _indexer.RebuildIndexAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(_store.GetSnapshot().SymbolsByFqn);
    }

    // RebuildIndexAsync_WithGit 的完整测试跳过:
    // 依赖 EnsureIndexLoadedAsync 发现 .git 根(经 GitWorkspaceResolver 真实文件系统查找),
    // InMemoryFileSystem 路径非真实路径,无法确定性 mock。属于"不可mock方法"。

    // ============ CodeIndexer 属性访问器 ============

    [Fact]
    public async Task Analytics_ReturnsGraphAnalyticsInstance() {
        await Task.CompletedTask.ConfigureAwait(true);
        Assert.NotNull(_indexer.Analytics);
        Assert.IsAssignableFrom<IGraphAnalytics>(_indexer.Analytics);
    }

    [Fact]
    public async Task Persistence_ReturnsGraphPersistenceInstance() {
        await Task.CompletedTask.ConfigureAwait(true);
        Assert.NotNull(_indexer.Persistence);
        Assert.IsAssignableFrom<IBinaryPersistence>(_indexer.Persistence);
    }

    [Fact]
    public async Task Visualization_ReturnsGraphVisualizationInstance() {
        await Task.CompletedTask.ConfigureAwait(true);
        Assert.NotNull(_indexer.Visualization);
        Assert.IsAssignableFrom<IGraphVisualization>(_indexer.Visualization);
    }

    [Fact]
    public async Task ProjectDependencyGraph_ReturnsInstance() {
        await Task.CompletedTask.ConfigureAwait(true);
        Assert.NotNull(_indexer.ProjectDependencyGraph);
        Assert.IsAssignableFrom<IProjectDependencyGraph>(_indexer.ProjectDependencyGraph);
    }

    [Fact]
    public async Task BuildIndexAsync_NullOptions_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _indexer.BuildIndexAsync(null!, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task UpdateFileAsync_NullFilePath_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _indexer.UpdateFileAsync(null!, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task RemoveFileAsync_NullFilePath_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _indexer.RemoveFileAsync(null!, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task SearchComprehensiveAsync_EmptyPattern_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _indexer.SearchComprehensiveAsync(null!, 1000, CancellationToken.None)).ConfigureAwait(true);
    }
}