namespace CodeIndex.Benchmarks;

public sealed class BddScenarioTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly InMemoryIndexStore _store;
    private readonly CodeIndexer _indexer;
    private readonly IFileSystem _fs = new IO.FileSystem.PhysicalFileSystem();

    public BddScenarioTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), $"bdd_{Guid.NewGuid():N}");
        _fs.CreateDirectory(_workspaceRoot);
        _store = new InMemoryIndexStore();
        _indexer = new CodeIndexer(_store, _fs);
    }

    public void Dispose()
    {
        try { _indexer.Dispose(); }
        finally { _store.Dispose(); }

        try { if (_fs.DirectoryExists(_workspaceRoot)) _fs.DeleteDirectory(_workspaceRoot, true); }
        catch (Exception ex) { Debug.WriteLine($"Failed to delete directory {_workspaceRoot}: {ex.Message}"); }
    }

    [Fact]
    public async Task Scenario_符号搜索_打开项目后可搜索类名()
    {
        // Given 一个包含 UserService.cs 的项目
        await _fs.WriteAllTextAsync(Path.Combine(_workspaceRoot, "UserService.cs"),
            "public class UserService { public string GetName() => \"User\"; }").ConfigureAwait(true);

        // When 构建索引并搜索 "UserService"
        var options = new CodeIndexOptions { WorkspaceRoot = _workspaceRoot };
        await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);
        var result = await _indexer.Searcher.SearchAsync("UserService", CancellationToken.None).ConfigureAwait(true);

        // Then 搜索结果包含 UserService 类
        Assert.Contains(result.Items, i => i.Name == "UserService" && i.Kind == SymbolKind.Class);
    }

    [Fact]
    public async Task Scenario_增量更新_修改文件后搜索结果更新()
    {
        // Given 已索引的 Calculator.cs
        await _fs.WriteAllTextAsync(Path.Combine(_workspaceRoot, "Calculator.cs"),
            "public class Calculator { public int Add(int a, int b) => a + b; }").ConfigureAwait(true);
        var options = new CodeIndexOptions { WorkspaceRoot = _workspaceRoot };
        await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        // When 修改文件添加 Subtract 方法
        await _fs.WriteAllTextAsync(Path.Combine(_workspaceRoot, "Calculator.cs"),
            "public class Calculator { public int Add(int a, int b) => a + b; public int Subtract(int a, int b) => a - b; }").ConfigureAwait(true);
        await _indexer.UpdateFileAsync(Path.Combine(_workspaceRoot, "Calculator.cs"), CancellationToken.None).ConfigureAwait(true);

        // Then 搜索 Subtract 应返回结果
        var result = await _indexer.Searcher.SearchAsync("Subtract", CancellationToken.None).ConfigureAwait(true);
        Assert.Contains(result.Items, i => i.Name == "Subtract");
    }

    [Fact]
    public async Task Scenario_调用图_查找方法调用者()
    {
        await _fs.WriteAllTextAsync(Path.Combine(_workspaceRoot, "UserService.cs"),
            "public class UserService { public string GetName() => \"User\"; }").ConfigureAwait(true);
        await _fs.WriteAllTextAsync(Path.Combine(_workspaceRoot, "Controller.cs"),
            """
            public class Controller
            {
                private readonly UserService _svc;
                public Controller(UserService svc) { _svc = svc; }
                public string Handle() { return _svc.GetName(); }
            }
            """).ConfigureAwait(true);
        var options = new CodeIndexOptions { WorkspaceRoot = _workspaceRoot };
        await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        var callers = await _indexer.CallGraph.GetCallersAsync("UserService.GetName", CancellationToken.None).ConfigureAwait(true);

        Assert.Contains(callers, e => e.CallerSymbol == "Controller.Handle" && e.CalleeSymbol == "UserService.GetName");
    }

    [Fact]
    public async Task Scenario_影响范围_方法变更影响相关文件()
    {
        await _fs.WriteAllTextAsync(Path.Combine(_workspaceRoot, "UserService.cs"),
            "public class UserService { public bool ValidateUser() => true; }").ConfigureAwait(true);
        await _fs.WriteAllTextAsync(Path.Combine(_workspaceRoot, "OrderService.cs"),
            """
            public class OrderService
            {
                private readonly UserService _user;
                public OrderService(UserService user) { _user = user; }
                public string Create() { return _user.ValidateUser() ? "OK" : "Invalid"; }
            }
            """).ConfigureAwait(true);
        var options = new CodeIndexOptions { WorkspaceRoot = _workspaceRoot };
        await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        var impact = await _indexer.CallGraph.GetImpactScopeAsync("UserService.ValidateUser", CancellationToken.None).ConfigureAwait(true);

        Assert.NotEmpty(impact);
    }

    [Fact]
    public async Task Scenario_跨文件定义查找()
    {
        // Given UserService 定义在 UserService.cs
        await _fs.WriteAllTextAsync(Path.Combine(_workspaceRoot, "UserService.cs"),
            "public class UserService { public string GetName() => \"User\"; }").ConfigureAwait(true);
        await _fs.WriteAllTextAsync(Path.Combine(_workspaceRoot, "Controller.cs"),
            "public class Controller { private readonly UserService _svc; public string Handle() { return _svc.GetName(); } }").ConfigureAwait(true);
        var options = new CodeIndexOptions { WorkspaceRoot = _workspaceRoot };
        await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        // When 查找 UserService 的定义
        var definition = await _indexer.Searcher.FindDefinitionAsync("UserService", CancellationToken.None).ConfigureAwait(true);

        // Then 定义指向 UserService.cs
        Assert.NotNull(definition);
        Assert.Equal("UserService", definition.Name);
        Assert.Contains("UserService.cs", definition.FilePath);
    }

    [Fact]
    public async Task Scenario_删除文件后索引更新()
    {
        // Given 已索引两个文件
        await _fs.WriteAllTextAsync(Path.Combine(_workspaceRoot, "ServiceA.cs"), "public class ServiceA { }").ConfigureAwait(true);
        await _fs.WriteAllTextAsync(Path.Combine(_workspaceRoot, "ServiceB.cs"), "public class ServiceB { }").ConfigureAwait(true);
        var options = new CodeIndexOptions { WorkspaceRoot = _workspaceRoot };
        await _indexer.BuildIndexAsync(options, CancellationToken.None).ConfigureAwait(true);

        // When 删除 ServiceA.cs 并从索引移除
        await _indexer.RemoveFileAsync(Path.Combine(_workspaceRoot, "ServiceA.cs"), CancellationToken.None).ConfigureAwait(true);

        // Then 搜索 ServiceA 不再返回结果
        var result = await _indexer.Searcher.SearchAsync("ServiceA", CancellationToken.None).ConfigureAwait(true);
        Assert.DoesNotContain(result.Items, i => i.Name == "ServiceA" && i.FilePath.Contains("ServiceA.cs"));
    }
}
