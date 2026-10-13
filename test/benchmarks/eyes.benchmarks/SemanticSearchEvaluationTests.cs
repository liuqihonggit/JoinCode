// JCC11003 抑制: 测试可空抑制 — _indexer 在模型存在时初始化，IsAvailable 守卫
#pragma warning disable JCC11003
namespace JoinCode.CodeIndex.Benchmarks;

/// <summary>
/// 向量语义搜索召回率评估 — 100 道题，用真实项目代码索引验证。
/// <para>查询：函数名 → 期望召回：函数所在文件（模糊匹配 EndsWith）。</para>
/// <para>指标：HitRate@10、MRR、P95 延迟。</para>
/// <para>前置：已运行 /code-index 构建索引 + ONNX 模型存在。CI 无模型时自动跳过。</para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class SemanticSearchEvaluationTests : IDisposable {
    private readonly PhysicalFileSystem _fs;
    private readonly CodeIndexer? _indexer;
    private readonly SemanticEvaluationEngine _engine;
    private bool _disposed;

    private const int TopK = 10;

    public SemanticSearchEvaluationTests() {
        _fs = new PhysicalFileSystem();
        _engine = new SemanticEvaluationEngine();

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "jcc", "embedding");
        var modelPath = Path.Combine(appData, "model_quantized.onnx");
        var vocabPath = Path.Combine(appData, "vocab.txt");

        if (!_fs.FileExists(modelPath) || !_fs.FileExists(vocabPath)) {
            return;
        }

        var store = new InMemoryIndexStore();
        _indexer = new CodeIndexer(store, _fs);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        try { _indexer?.Dispose(); } catch (ObjectDisposedException) { Console.WriteLine("CodeIndexer already disposed"); }
        _fs?.Dispose();
    }

    private bool IsAvailable => _indexer is not null;

    /// <summary>
    /// 加载已构建的索引（需先运行 /code-index）。
    /// </summary>
    private async Task<bool> EnsureIndexLoadedAsync() {
        if (!IsAvailable) return false;
        await _indexer!.EnsureIndexLoadedAsync(CancellationToken.None);
        return true;
    }

    /// <summary>
    /// 运行全部 100 题，评估 HitRate@10。
    /// </summary>
    [Fact]
    public async Task Semantic_HitRateAt10_AboveThreshold() {
        if (!await EnsureIndexLoadedAsync()) return;
        var indexer = _indexer!;

        var cases = SemanticTestCaseRepository.GetCases();
        var results = new List<SemanticEvaluationResult>(cases.Count);

        foreach (var tc in cases) {
            var sw = Stopwatch.StartNew();
            var searchResults = await indexer
                .SearchSemanticAsync(tc.Query, TopK, CancellationToken.None);
            sw.Stop();
            results.Add(_engine.Evaluate(tc, searchResults, sw.ElapsedMilliseconds));
        }

        var summary = _engine.Summarize("Semantic@10", results);
        OutputSummary(summary, results);

        Assert.True(summary.HitRate >= 0.10,
            $"召回失败: HitRate@10={summary.HitRate:P1} < 10% 基线。未命中 {summary.MissedIds.Length}/{summary.TotalCases} 题。");
    }

    /// <summary>
    /// MRR (Mean Reciprocal Rank) 应 >= 0.15 — 衡量命中排序质量。
    /// </summary>
    [Fact]
    public async Task Semantic_Mrr_AboveThreshold() {
        if (!await EnsureIndexLoadedAsync()) return;
        var indexer = _indexer!;

        var cases = SemanticTestCaseRepository.GetCases();
        var results = new List<SemanticEvaluationResult>(cases.Count);

        foreach (var tc in cases) {
            var sw = Stopwatch.StartNew();
            var searchResults = await indexer
                .SearchSemanticAsync(tc.Query, TopK, CancellationToken.None);
            sw.Stop();
            results.Add(_engine.Evaluate(tc, searchResults, sw.ElapsedMilliseconds));
        }

        var summary = _engine.Summarize("MRR", results);
        OutputSummary(summary, results);

        Assert.True(summary.Mrr >= 0.05,
            $"召回失败: MRR={summary.Mrr:F4} < 0.05 基线。排序质量不足。");
    }

    /// <summary>
    /// P95 延迟应 < 500ms — 单次语义搜索响应时间。
    /// </summary>
    [Fact]
    public async Task Semantic_P95Latency_Under500ms() {
        if (!await EnsureIndexLoadedAsync()) return;
        var indexer = _indexer!;

        var cases = SemanticTestCaseRepository.GetCases();
        var results = new List<SemanticEvaluationResult>(cases.Count);

        foreach (var tc in cases) {
            var sw = Stopwatch.StartNew();
            var searchResults = await indexer
                .SearchSemanticAsync(tc.Query, TopK, CancellationToken.None);
            sw.Stop();
            results.Add(_engine.Evaluate(tc, searchResults, sw.ElapsedMilliseconds));
        }

        var summary = _engine.Summarize("Latency", results);
        OutputSummary(summary, results);

        Assert.True(summary.P95Ms < 500,
            $"P95={summary.P95Ms:F1}ms >= 500ms 阈值。");
    }

    private static void OutputSummary(SemanticEvaluationSummary summary, IReadOnlyList<SemanticEvaluationResult> results) {
        var missed = results.Where(r => !r.Hit).ToList();
        var missedDetail = missed.Count > 0
            ? string.Join("\n", missed.Take(20).Select(r => $"  {r.TestCaseId}: query=\"{r.Query}\" expected=\"{r.ExpectedFile}\" got=[{string.Join(", ", r.TopKFiles.Take(3))}]"))
            : "  (无)";
        Console.WriteLine(
            $"""
            [{summary.Layer}] {summary.HitCount}/{summary.TotalCases} 命中
              HitRate={summary.HitRate:P1}  MRR={summary.Mrr:F4}  AvgResults={summary.AvgResultCount:F1}
              P50={summary.P50Ms:F1}ms  P95={summary.P95Ms:F1}ms  P99={summary.P99Ms:F1}ms
              未命中 ({missed.Count}):
            {missedDetail}
            """);
    }
}
