namespace JoinCode.CodeIndex.Benchmarks;

[Trait("Category", "Benchmark")]
public sealed class PerformanceBenchmarkTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly IFileSystem _fs = new IO.FileSystem.PhysicalFileSystem();

    public PerformanceBenchmarkTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), $"perf_{Guid.NewGuid():N}");
        _fs.CreateDirectory(_workspaceRoot);
    }

    public void Dispose()
    {
        try { if (_fs.DirectoryExists(_workspaceRoot)) _fs.DeleteDirectory(_workspaceRoot, true); }
        catch (Exception ex) { Debug.WriteLine($"Failed to delete directory {_workspaceRoot}: {ex.Message}"); }
    }

    [Fact]
    public async Task IndexBuild_SmallWorkspace_Under5Seconds()
    {
        GenerateFiles(10, 50);
        using var store = new InMemoryIndexStore();
        using var indexer = new CodeIndexer(store, _fs);
        var options = new CodeIndexOptions { WorkspaceRoot = _workspaceRoot };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 5000, $"10文件索引构建耗时 {sw.ElapsedMilliseconds}ms > 5000ms");
    }

    [Fact]
    public async Task IndexBuild_MediumWorkspace_Under30Seconds()
    {
        GenerateFiles(100, 100);
        using var store = new InMemoryIndexStore();
        using var indexer = new CodeIndexer(store, _fs);
        var options = new CodeIndexOptions { WorkspaceRoot = _workspaceRoot };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 30000, $"100文件索引构建耗时 {sw.ElapsedMilliseconds}ms > 30000ms");
    }

    [Fact]
    public async Task IncrementalUpdate_SingleFile_Under500ms()
    {
        GenerateFiles(20, 80);
        using var store = new InMemoryIndexStore();
        using var indexer = new CodeIndexer(store, _fs);
        var options = new CodeIndexOptions { WorkspaceRoot = _workspaceRoot };
        await indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        var filePath = Path.Combine(_workspaceRoot, "Service_0.cs");
        await _fs.WriteAllTextAsync(filePath, "public class UpdatedService { public void NewMethod() { } }").ConfigureAwait(true);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await indexer.UpdateFileAsync(filePath, CancellationToken.None).ConfigureAwait(true);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500, $"单文件增量更新耗时 {sw.ElapsedMilliseconds}ms > 500ms");
    }

    [Fact]
    public async Task QueryLatency_SearchUnder50ms()
    {
        GenerateFiles(50, 100);
        using var store = new InMemoryIndexStore();
        using var indexer = new CodeIndexer(store, _fs);
        var options = new CodeIndexOptions { WorkspaceRoot = _workspaceRoot };
        await indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await indexer.Searcher.SearchAsync("Service", CancellationToken.None).ConfigureAwait(true);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 50, $"搜索查询耗时 {sw.ElapsedMilliseconds}ms > 50ms");
        Assert.True(result.TotalCount > 0, "搜索应返回结果");
    }

    [Fact]
    public async Task QueryLatency_CallGraphUnder100ms()
    {
        GenerateFiles(30, 80);
        using var store = new InMemoryIndexStore();
        using var indexer = new CodeIndexer(store, _fs);
        var options = new CodeIndexOptions { WorkspaceRoot = _workspaceRoot };
        await indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var callers = await indexer.CallGraph.GetCallersAsync("DoWork", CancellationToken.None).ConfigureAwait(true);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 100, $"调用图查询耗时 {sw.ElapsedMilliseconds}ms > 100ms");
    }

    [Fact]
    public async Task MemoryUsage_L1L2Under600MB()
    {
        GenerateFiles(100, 100);
        using var store = new InMemoryIndexStore();
        using var indexer = new CodeIndexer(store, _fs);
        var options = new CodeIndexOptions { WorkspaceRoot = _workspaceRoot };
        await indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memoryMB = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
        Assert.True(memoryMB < 600, $"内存占用 {memoryMB:F0}MB > 600MB");
    }

    private void GenerateFiles(int fileCount, int linesPerFile)
    {
        for (var i = 0; i < fileCount; i++)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"public class Service_{i} {{");
            for (var j = 0; j < linesPerFile / 5; j++)
            {
                sb.AppendLine($"    public int Prop_{j} {{ get; set; }}");
            }
            sb.AppendLine($"    public void DoWork() {{ Helper_{i}.Process(); }}");
            sb.AppendLine($"    public string GetName() => \"Service_{i}\";");
            sb.AppendLine("}");
            sb.AppendLine($"public static class Helper_{i} {{");
            sb.AppendLine($"    public static void Process() {{ }}");
            sb.AppendLine("}");
            _fs.WriteAllText(Path.Combine(_workspaceRoot, $"Service_{i}.cs"), sb.ToString());
        }
    }
}
