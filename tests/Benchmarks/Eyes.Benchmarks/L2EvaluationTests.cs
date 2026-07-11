namespace CodeIndex.Benchmarks;

public sealed class L2EvaluationTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly InMemoryIndexStore _store;
    private readonly CodeIndexer _indexer;
    private readonly EvaluationEngine _engine;
    private readonly IFileSystem _fs = new IO.FileSystem.PhysicalFileSystem();

    public L2EvaluationTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), $"bench_l2_{Guid.NewGuid():N}");
        _fs.CreateDirectory(_workspaceRoot);
        _store = new InMemoryIndexStore();
        _indexer = new CodeIndexer(_store, _fs);
        _engine = new EvaluationEngine();
        SeedWorkspace();
    }

    public void Dispose()
    {
        try { _indexer.Dispose(); }
        finally { _store.Dispose(); }

        try { if (_fs.DirectoryExists(_workspaceRoot)) _fs.DeleteDirectory(_workspaceRoot, true); }
        catch (Exception ex) { Debug.WriteLine($"Failed to delete directory {_workspaceRoot}: {ex.Message}"); }
    }

    private void SeedWorkspace()
    {
        _fs.WriteAllText(Path.Combine(_workspaceRoot, "UserService.cs"), """
            using System;
            namespace MyApp.Services {
                public class UserService {
                    public int UserId { get; set; }
                    public string UserName { get; set; }
                    public UserService(int userId, string userName) { UserId = userId; UserName = userName; }
                    public string GetName() => UserName;
                    public string GetDisplayName() => $"{UserName} ({UserId})";
                    public bool ValidateUser() => UserId > 0 && !string.IsNullOrEmpty(UserName);
                }
            }
            """);
_fs.WriteAllText(Path.Combine(_workspaceRoot, "OrderService.cs"), """
            namespace MyApp.Services {
                public class OrderService {
                    private readonly UserService _user;
                    public OrderService(UserService user) { _user = user; }
                    public string CreateOrder() {
                        if (_user.ValidateUser()) return _user.GetDisplayName();
                        return "Invalid user";
                    }
                }
            }
            """);
_fs.WriteAllText(Path.Combine(_workspaceRoot, "Repository.cs"), """
            using System.Collections.Generic;
            public interface IRepository<T> {
                T GetById(int id);
                IEnumerable<T> GetAll();
                void Save(T entity);
            }
            public class Repository<T> : IRepository<T> {
                public T GetById(int id) => default;
                public IEnumerable<T> GetAll() => [];
                public void Save(T entity) { }
            }
            """);
_fs.WriteAllText(Path.Combine(_workspaceRoot, "Calculator.cs"), """
            public class Calculator {
                public int Compute(int a, int b) { return Helper.Square(a) + Helper.Square(b); }
                public int Sum(int a, int b) { return Helper.Square(a + b); }
            }
            public static class Helper {
                public static int Square(int x) => x * x;
            }
            """);
_fs.WriteAllText(Path.Combine(_workspaceRoot, "Controller.cs"), """
            public class Controller {
                private readonly UserService _svc;
                public Controller(UserService svc) { _svc = svc; }
                public string Handle() { return _svc.GetName(); }
            }
            """);

        var options = new CodeIndexOptions { WorkspaceRoot = _workspaceRoot };
        _indexer.BuildIndexAsync(options, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task L2_CallerCallee_PassRateAbove70()
    {
        var results = await Task.WhenAll(
            TestCaseRepository.GetL2TestCases()
                .Where(c => c.Category == "caller_callee")
                .Select(EvaluateCallerCalleeAsync)).ConfigureAwait(true);

        var summary = _engine.Summarize("L2-caller_callee", results);
        Assert.True(summary.PassRate >= 0.7, $"L2 调用者/被调用者通过率 {summary.PassRate:P} < 70%");
    }

    [Fact]
    public async Task L2_ImpactScope_ReturnsRelevantSymbols()
    {
        var anyNonEmpty = (await Task.WhenAll(
                TestCaseRepository.GetL2TestCases()
                    .Where(c => c.Category == "impact_scope")
                    .Select(QueryImpactScopeAsync)).ConfigureAwait(true))
            .Any(affected => affected.Count > 0);

        Assert.True(anyNonEmpty, "至少一个影响范围查询应返回非空结果");
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task L2_AllCases_ResponseTimeUnder200ms()
    {
        var results = await Task.WhenAll(
            TestCaseRepository.GetL2TestCases()
                .Select(EvaluateL2CaseAsync)).ConfigureAwait(true);

        var summary = _engine.Summarize("L2-all", results);
        Assert.True(summary.P95Ms < 200, $"L2 P95 响应时间 {summary.P95Ms}ms > 200ms");
    }

    private async Task<EvaluationResult> EvaluateCallerCalleeAsync(TestCase tc)
    {
        var sw = Stopwatch.StartNew();
        var edges = tc.Query == "callers"
            ? await _indexer.CallGraph.GetCallersAsync(tc.SourceSymbol!, CancellationToken.None).ConfigureAwait(true)
            : await _indexer.CallGraph.GetCalleesAsync(tc.SourceSymbol!, CancellationToken.None).ConfigureAwait(true);
        sw.Stop();
        return _engine.EvaluateL2CallGraph(tc, edges, sw.ElapsedMilliseconds);
    }

    private async Task<IReadOnlyList<string>> QueryImpactScopeAsync(TestCase tc) =>
        await _indexer.CallGraph.GetImpactScopeAsync(tc.SourceSymbol!, CancellationToken.None).ConfigureAwait(true);

    private async Task<EvaluationResult> EvaluateL2CaseAsync(TestCase tc)
    {
        var sw = Stopwatch.StartNew();

        if (tc.Query == "callers")
        {
            var edges = await _indexer.CallGraph.GetCallersAsync(tc.SourceSymbol!, CancellationToken.None).ConfigureAwait(true);
            sw.Stop();
            return _engine.EvaluateL2CallGraph(tc, edges, sw.ElapsedMilliseconds);
        }

        if (tc.Query == "callees")
        {
            var edges = await _indexer.CallGraph.GetCalleesAsync(tc.SourceSymbol!, CancellationToken.None).ConfigureAwait(true);
            sw.Stop();
            return _engine.EvaluateL2CallGraph(tc, edges, sw.ElapsedMilliseconds);
        }

        if (tc.Query == "impact")
        {
            var affected = await _indexer.CallGraph.GetImpactScopeAsync(tc.SourceSymbol!, CancellationToken.None).ConfigureAwait(true);
            sw.Stop();
            return _engine.EvaluateL2ImpactScope(tc, affected, sw.ElapsedMilliseconds);
        }

        sw.Stop();
        return new EvaluationResult
        {
            TestCaseId = tc.Id, Category = tc.Category, Passed = false,
            Recall = 0, Precision = 0, F1 = 0, ElapsedMs = sw.ElapsedMilliseconds,
            ActualResults = [], MissingResults = tc.ExpectedResults, ExtraResults = []
        };
    }
}
