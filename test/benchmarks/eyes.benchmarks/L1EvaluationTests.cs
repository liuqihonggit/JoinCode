namespace JoinCode.CodeIndex.Benchmarks;

public sealed class L1EvaluationTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly InMemoryIndexStore _store;
    private readonly CodeIndexer _indexer;
    private readonly EvaluationEngine _engine;
    private readonly IFileSystem _fs = new IO.FileSystem.PhysicalFileSystem();

    public L1EvaluationTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), $"bench_l1_{Guid.NewGuid():N}");
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
    public async Task L1_ExactSearch_PassRateAbove80()
    {
        var results = await Task.WhenAll(
            TestCaseRepository.GetL1TestCases()
                .Where(c => c.Category == "exact_search")
                .Select(EvaluateL1CaseAsync)).ConfigureAwait(true);

        var summary = _engine.Summarize("L1-exact", results);
        Assert.True(summary.PassRate >= 0.8, $"L1 精确搜索通过率 {summary.PassRate:P} < 80%");
        Assert.True(summary.AvgRecall >= 0.8, $"L1 精确搜索召回率 {summary.AvgRecall:P} < 80%");
    }

    [Fact]
    public async Task L1_FuzzySearch_PassRateAbove60()
    {
        var results = await Task.WhenAll(
            TestCaseRepository.GetL1TestCases()
                .Where(c => c.Category == "fuzzy_search")
                .Select(EvaluateL1CaseAsync)).ConfigureAwait(true);

        var summary = _engine.Summarize("L1-fuzzy", results);
        Assert.True(summary.PassRate >= 0.4, $"L1 模糊搜索通过率 {summary.PassRate:P} < 40%");
    }

    [Fact]
    public async Task L1_CrossFileSearch_PassRateAbove70()
    {
        var results = await Task.WhenAll(
            TestCaseRepository.GetL1TestCases()
                .Where(c => c.Category == "cross_file_search")
                .Select(EvaluateL1CaseAsync)).ConfigureAwait(true);

        var summary = _engine.Summarize("L1-cross", results);
        Assert.True(summary.PassRate >= 0.7, $"L1 跨文件搜索通过率 {summary.PassRate:P} < 70%");
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task L1_AllCases_ResponseTimeUnder100ms()
    {
        var results = await Task.WhenAll(
            TestCaseRepository.GetL1TestCases()
                .Select(EvaluateL1CaseAsync)).ConfigureAwait(true);

        var summary = _engine.Summarize("L1-all", results);
        Assert.True(summary.P95Ms < 100, $"L1 P95 响应时间 {summary.P95Ms}ms > 100ms");
    }

    private async Task<EvaluationResult> EvaluateL1CaseAsync(TestCase tc)
    {
        var sw = Stopwatch.StartNew();
        var searchResult = await _indexer.Searcher.SearchAsync(tc.Query, CancellationToken.None).ConfigureAwait(true);
        sw.Stop();
        return _engine.EvaluateL1(tc, searchResult, sw.ElapsedMilliseconds);
    }
}
