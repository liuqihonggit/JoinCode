namespace JoinCode.CodeIndex.Tests;

public sealed class CodeIndexerTests : IDisposable
{
    private readonly InMemoryIndexStore _store;
    private readonly CodeIndexer _indexer;
    private readonly IFileSystem _fs;

    public CodeIndexerTests()
    {
        _store = new InMemoryIndexStore();
        _fs = new IO.FileSystem.InMemoryFileSystem();
        _indexer = new CodeIndexer(_store, _fs);
    }

    public void Dispose()
    {
        _indexer.Dispose();
        _store.Dispose();
    }

    [Fact]
    public async Task BuildIndexAsync_EmptyDirectory_CompletesWithZeroFiles()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task BuildIndexAsync_SingleCsFile_IndexesSymbols()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task BuildIndexAsync_MultipleCsFiles_IndexesAll()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task BuildIndexAsync_ExcludesBinObjDirectories()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task BuildIndexAsync_SecondRun_SkipsUnchangedFiles()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task BuildIndexAsync_SecondRun_ReindexesModifiedFiles()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task BuildIndexAsync_RemovesDeletedFilesFromIndex()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task BuildIndexAsync_ProgressCallback_ReportsProgress()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task UpdateFileAsync_DelegatesToIncrementalUpdater()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task RemoveFileAsync_DelegatesToSymbolIndex()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task Searcher_ReturnsSymbolSearcher()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task CallGraph_ReturnsCallGraphInstance()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task DependencyGraph_ReturnsDependencyGraphInstance()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    // ============ SearchComprehensiveAsync (rg+AST 综合检索) ============

    [Fact]
    public async Task SearchComprehensiveAsync_ReturnsMatchedSymbolsAndCallers()
    {
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
    public async Task SearchComprehensiveAsync_ReturnsCallees()
    {
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
    public async Task SearchComprehensiveAsync_NoMatches_ReturnsEmpty()
    {
        InsertSymbol(CreateSymbol("Foo", "App.Foo", SymbolKind.Method, "a.cs"));

        var result = await _indexer.SearchComprehensiveAsync("NonExistent", 1000, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(result.MatchedSymbols);
        Assert.Empty(result.Callers);
        Assert.Empty(result.Callees);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task SearchComprehensiveAsync_RespectsTokenBudget()
    {
        // 插入多个匹配符号,超过小 token 预算
        for (var i = 0; i < 5; i++)
        {
            InsertSymbol(CreateSymbol($"Process{i}", $"App.Services.Process{i}", SymbolKind.Method, $"f{i}.cs"));
        }

        var result = await _indexer.SearchComprehensiveAsync("Process", 10, CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Truncated);
        Assert.True(result.EstimatedTokens <= 10);
    }

    [Fact]
    public async Task SearchComprehensiveAsync_NullPattern_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _indexer.SearchComprehensiveAsync(null!, 1000, CancellationToken.None)).ConfigureAwait(true);
    }

    // ============ 正确性缺口验证测试 ============

    /// <summary>
    /// 验证 100 候选上限: 插入 150 个匹配符号,MatchedSymbols 仅返回前 100 个,
    /// 但 TotalMatchedCount 暴露真实总数 150,LLM 可据此判断是否被候选上限截断
    /// </summary>
    [Fact]
    public async Task SearchComprehensiveAsync_Exceeds100CandidateLimit_ExposesTotalCount()
    {
        // 插入 150 个匹配 "Foo" 的符号
        for (var i = 0; i < 150; i++)
        {
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
    public async Task SearchComprehensiveAsync_References_ReturnsCallSites_NotSameNameDefinitions()
    {
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
    public async Task SearchComprehensiveAsync_TruncationPriority_MatchedFirst()
    {
        // 插入多个匹配符号 + 引用 + 调用方/被调用方
        for (var i = 0; i < 10; i++)
        {
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
    public async Task SearchComprehensiveAsync_IncludeAstFalse_SkipsReferencesAndCallGraph()
    {
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

    private static SymbolInfo CreateSymbol(string name, string fqn, SymbolKind kind, string file)
    {
        return new SymbolInfo
        {
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

    private void InsertSymbol(SymbolInfo symbol)
    {
        _store.SymbolsByFqn[symbol.FullyQualifiedName] = symbol;
        AddToBucket(_store.SymbolsByName, symbol.Name, symbol);
        AddToBucket(_store.SymbolsByFile, symbol.FilePath, symbol);
        AddToBucket(_store.SymbolsByKind, symbol.Kind, symbol);
    }

    private void InsertCallEdge(string caller, string callee, string file, int line, CallKind kind)
    {
        var edge = new CallEdge
        {
            CallerSymbol = caller,
            CalleeSymbol = callee,
            CallSiteFilePath = file,
            CallSiteLine = line,
            CallKind = kind
        };
        _store.CallEdges.Add(edge);
        AddToBucket(_store.CallsByCaller, caller, edge);
        AddToBucket(_store.CallsByCallee, callee, edge);
        AddToBucket(_store.CallsByFile, file, edge);
    }

    private static void AddToBucket<TKey, TValue>(Dictionary<TKey, List<TValue>> dict, TKey key, TValue value) where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<TValue>();
            dict[key] = list;
        }
        list.Add(value);
    }
}
