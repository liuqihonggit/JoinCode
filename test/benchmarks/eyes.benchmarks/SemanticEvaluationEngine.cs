namespace JoinCode.CodeIndex.Benchmarks;

/// <summary>
/// 语义搜索评估引擎 — 模糊匹配（文件名包含）、MRR、HitRate、延迟百分位。
/// </summary>
public sealed class SemanticEvaluationEngine {
    /// <summary>
    /// 评估单题 — 检查 Top-K 结果中是否有 FilePath 包含期望文件名。
    /// </summary>
    public SemanticEvaluationResult Evaluate(
        SemanticTestCase testCase,
        IReadOnlyList<ChunkSearchResult> searchResults,
        long elapsedMs) {
        ArgumentNullException.ThrowIfNull(testCase);
        ArgumentNullException.ThrowIfNull(searchResults);

        var topKFiles = searchResults
            .Select(r => r.FilePath)
            .ToArray();

        var hitRank = 0;
        var hitScore = 0f;
        for (var i = 0; i < searchResults.Count; i++) {
            var filePath = searchResults[i].FilePath;
            if (filePath.EndsWith(testCase.ExpectedFile, StringComparison.OrdinalIgnoreCase)) {
                hitRank = i + 1;
                hitScore = searchResults[i].Score;
                break;
            }
        }

        return new SemanticEvaluationResult {
            TestCaseId = testCase.Id,
            Query = testCase.Query,
            ExpectedFile = testCase.ExpectedFile,
            Hit = hitRank > 0,
            Rank = hitRank,
            Score = hitScore,
            ResultCount = searchResults.Count,
            ElapsedMs = elapsedMs,
            TopKFiles = topKFiles
        };
    }

    /// <summary>
    /// 汇总评估结果 — HitRate、MRR、延迟百分位。
    /// </summary>
    public SemanticEvaluationSummary Summarize(string layer, IReadOnlyList<SemanticEvaluationResult> results) {
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count == 0) {
            return new SemanticEvaluationSummary {
                Layer = layer,
                TotalCases = 0,
                HitCount = 0,
                HitRate = 0,
                Mrr = 0,
                AvgResultCount = 0,
                P50Ms = 0,
                P95Ms = 0,
                P99Ms = 0,
                MissedIds = []
            };
        }

        var hitCount = results.Count(r => r.Hit);
        var mrr = results.Average(r => r.Hit ? 1.0 / r.Rank : 0.0);
        var avgResultCount = results.Average(r => (double)r.ResultCount);
        var sortedMs = results
            .Select(r => (double)r.ElapsedMs)
            .OrderBy(x => x)
            .ToList();
        var missedIds = results
            .Where(r => !r.Hit)
            .Select(r => r.TestCaseId)
            .ToArray();

        return new SemanticEvaluationSummary {
            Layer = layer,
            TotalCases = results.Count,
            HitCount = hitCount,
            HitRate = (double)hitCount / results.Count,
            Mrr = mrr,
            AvgResultCount = avgResultCount,
            P50Ms = Percentile(sortedMs, 50),
            P95Ms = Percentile(sortedMs, 95),
            P99Ms = Percentile(sortedMs, 99),
            MissedIds = missedIds
        };
    }

    private static double Percentile(List<double> sorted, int percentile) {
        if (sorted.Count == 0) return 0;
        var index = (percentile / 100.0) * (sorted.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
    }
}
