#pragma warning disable JCC9001, JCC9002
namespace JoinCode.CodeIndex.Tests;

public sealed class CallGraphTests : IDisposable
{
    private readonly InMemoryIndexStore _store;
    private readonly CallGraph _callGraph;

    public CallGraphTests()
    {
        _store = new InMemoryIndexStore();
        _callGraph = new CallGraph(_store);
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public async Task GetCallersAsync_ReturnsCallersOfSymbol()
    {
        InsertCallEdge("Process", "Validate", "svc.cs", 10, CallKind.Direct);
        InsertCallEdge("Handle", "Validate", "handler.cs", 20, CallKind.Direct);

        var callers = await _callGraph.GetCallersAsync("Validate", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, callers.Count);
        Assert.Contains(callers, c => c.CallerSymbol == "Process");
        Assert.Contains(callers, c => c.CallerSymbol == "Handle");
    }

    [Fact]
    public async Task GetCalleesAsync_ReturnsCalleesOfSymbol()
    {
        InsertCallEdge("Process", "Validate", "svc.cs", 10, CallKind.Direct);
        InsertCallEdge("Process", "Save", "svc.cs", 15, CallKind.Direct);

        var callees = await _callGraph.GetCalleesAsync("Process", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, callees.Count);
        Assert.Contains(callees, c => c.CalleeSymbol == "Validate");
        Assert.Contains(callees, c => c.CalleeSymbol == "Save");
    }

    [Fact]
    public async Task GetCallChainAsync_DirectChain_ReturnsPath()
    {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("B", "C", "b.cs", 1, CallKind.Direct);

        var chain = await _callGraph.GetCallChainAsync("A", "C", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, chain.Count);
        Assert.Equal("A", chain[0].CallerSymbol);
        Assert.Equal("B", chain[0].CalleeSymbol);
        Assert.Equal("B", chain[1].CallerSymbol);
        Assert.Equal("C", chain[1].CalleeSymbol);
    }

    [Fact]
    public async Task GetCallChainAsync_NoPath_ReturnsEmpty()
    {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("C", "D", "c.cs", 1, CallKind.Direct);

        var chain = await _callGraph.GetCallChainAsync("A", "D", CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(chain);
    }

    [Fact]
    public async Task GetImpactScopeAsync_ReturnsAllAffectedSymbols()
    {
        InsertCallEdge("Process", "Validate", "svc.cs", 10, CallKind.Direct);
        InsertCallEdge("Handle", "Process", "handler.cs", 5, CallKind.Direct);
        InsertCallEdge("Run", "Handle", "main.cs", 1, CallKind.Direct);

        var impact = await _callGraph.GetImpactScopeAsync("Validate", CancellationToken.None).ConfigureAwait(true);

        Assert.Contains("Process", impact);
        Assert.Contains("Handle", impact);
        Assert.Contains("Run", impact);
    }

    [Fact]
    public async Task GetCallersAsync_NoCallers_ReturnsEmpty()
    {
        var callers = await _callGraph.GetCallersAsync("NonExistent", CancellationToken.None).ConfigureAwait(true);
        Assert.Empty(callers);
    }

    [Fact]
    public async Task InvalidateCacheForFile_ThenQuery_ReturnsUpdatedResults()
    {
        InsertCallEdge("A", "B", "old.cs", 1, CallKind.Direct);

        var callersBefore = await _callGraph.GetCallersAsync("B", CancellationToken.None).ConfigureAwait(true);
        Assert.Single(callersBefore);
        Assert.Equal("A", callersBefore[0].CallerSymbol);

        DeleteCallEdgesForFile("old.cs");
        await _callGraph.InvalidateCacheForFileAsync("old.cs", CancellationToken.None).ConfigureAwait(true);

        InsertCallEdge("C", "B", "new.cs", 1, CallKind.Direct);
        await _callGraph.InvalidateCacheForFileAsync("new.cs", CancellationToken.None).ConfigureAwait(true);

        var callersAfter = await _callGraph.GetCallersAsync("B", CancellationToken.None).ConfigureAwait(true);
        Assert.Single(callersAfter);
        Assert.Equal("C", callersAfter[0].CallerSymbol);
    }

    [Fact]
    public async Task InvalidateCacheForFile_MultipleFiles_UpdatesBoth()
    {
        InsertCallEdge("A", "B", "file1.cs", 1, CallKind.Direct);
        InsertCallEdge("C", "D", "file2.cs", 1, CallKind.Direct);

        DeleteCallEdgesForFile("file1.cs");
        DeleteCallEdgesForFile("file2.cs");
        await _callGraph.InvalidateCacheForFileAsync("file1.cs", CancellationToken.None).ConfigureAwait(true);
        await _callGraph.InvalidateCacheForFileAsync("file2.cs", CancellationToken.None).ConfigureAwait(true);

        InsertCallEdge("A", "X", "file1.cs", 2, CallKind.Direct);
        InsertCallEdge("C", "Y", "file2.cs", 2, CallKind.Direct);
        await _callGraph.InvalidateCacheForFileAsync("file1.cs", CancellationToken.None).ConfigureAwait(true);
        await _callGraph.InvalidateCacheForFileAsync("file2.cs", CancellationToken.None).ConfigureAwait(true);

        var calleesOfA = await _callGraph.GetCalleesAsync("A", CancellationToken.None).ConfigureAwait(true);
        Assert.Single(calleesOfA);
        Assert.Equal("X", calleesOfA[0].CalleeSymbol);

        var calleesOfC = await _callGraph.GetCalleesAsync("C", CancellationToken.None).ConfigureAwait(true);
        Assert.Single(calleesOfC);
        Assert.Equal("Y", calleesOfC[0].CalleeSymbol);
    }

    [Fact]
    public async Task GetCallChainAsync_WithCycle_ReturnsEmpty()
    {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("B", "C", "b.cs", 1, CallKind.Direct);
        InsertCallEdge("C", "A", "c.cs", 1, CallKind.Direct);

        var chain = await _callGraph.GetCallChainAsync("A", "A", CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(chain);
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

    private void DeleteCallEdgesForFile(string filePath)
    {
        _store.CallEdges.RemoveAll(e => e.CallSiteFilePath == filePath);
        foreach (var kv in _store.CallsByCaller)
        {
            kv.Value.RemoveAll(e => e.CallSiteFilePath == filePath);
        }
        foreach (var kv in _store.CallsByCallee)
        {
            kv.Value.RemoveAll(e => e.CallSiteFilePath == filePath);
        }
        _store.CallsByFile.Remove(filePath);
    }

    private static void AddToBucket<TKey>(Dictionary<TKey, List<CallEdge>> dict, TKey key, CallEdge edge) where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<CallEdge>();
            dict[key] = list;
        }
        list.Add(edge);
    }
}
