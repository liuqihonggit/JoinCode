namespace JoinCode.CodeIndex.Benchmarks;

public sealed class EvaluationEngine
{
    public EvaluationResult EvaluateL1(TestCase testCase, SearchResult<SymbolInfo> searchResult, long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        ArgumentNullException.ThrowIfNull(searchResult);

        var actualNames = searchResult.Items
            .SelectMany(ExpandSymbolNames)
            .ToHashSet();

        return EvaluateCore(testCase, actualNames, elapsedMs, includeExtra: true);
    }

    public EvaluationResult EvaluateL2CallGraph(TestCase testCase, IReadOnlyList<CallEdge> edges, long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        var actualNames = edges
            .Select(FormatCallEdge)
            .ToHashSet();

        return EvaluateCore(testCase, actualNames, elapsedMs);
    }

    public EvaluationResult EvaluateL2ImpactScope(TestCase testCase, IReadOnlyList<string> affectedFiles, long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        var actualPaths = affectedFiles
            .Select(ExtractFileName)
            .ToHashSet();

        return EvaluateCore(testCase, actualPaths, elapsedMs);
    }

    public EvaluationSummary Summarize(string layer, IReadOnlyList<EvaluationResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count == 0)
        {
            return new EvaluationSummary
            {
                Layer = layer,
                TotalCases = 0,
                PassedCases = 0,
                PassRate = 0,
                AvgRecall = 0,
                AvgPrecision = 0,
                AvgF1 = 0,
                P50Ms = 0,
                P95Ms = 0,
                P99Ms = 0
            };
        }

        var passedCount = results.Count(r => r.Passed);
        var sortedMs = results
            .Select(r => (double)r.ElapsedMs)
            .OrderBy(x => x)
            .ToList();

        return new EvaluationSummary
        {
            Layer = layer,
            TotalCases = results.Count,
            PassedCases = passedCount,
            PassRate = (double)passedCount / results.Count,
            AvgRecall = results.Average(r => r.Recall),
            AvgPrecision = results.Average(r => r.Precision),
            AvgF1 = results.Average(r => r.F1),
            P50Ms = Percentile(sortedMs, 50),
            P95Ms = Percentile(sortedMs, 95),
            P99Ms = Percentile(sortedMs, 99)
        };
    }

    private static EvaluationResult EvaluateCore(
        TestCase testCase,
        HashSet<string> actualNames,
        long elapsedMs,
        bool includeExtra = false)
    {
        var expectedSet = testCase.ExpectedResults.ToHashSet();
        var found = expectedSet.Intersect(actualNames).ToHashSet();
        var missing = expectedSet.Except(actualNames).ToHashSet();

        var recall = expectedSet.Count > 0 ? (double)found.Count / expectedSet.Count : 0;
        var precision = actualNames.Count > 0 ? (double)found.Count / actualNames.Count : 0;
        var f1 = recall + precision > 0 ? 2 * recall * precision / (recall + precision) : 0;

        return new EvaluationResult
        {
            TestCaseId = testCase.Id,
            Category = testCase.Category,
            Passed = missing.Count == 0,
            Recall = recall,
            Precision = precision,
            F1 = f1,
            ElapsedMs = elapsedMs,
            ActualResults = [.. actualNames],
            MissingResults = [.. missing],
            ExtraResults = includeExtra ? [.. found] : []
        };
    }

#pragma warning disable JCC7001 // 方法组引用 SelectMany(ExpandSymbolNames)，MSBuildWorkspace无法解析
    private static IEnumerable<string> ExpandSymbolNames(SymbolInfo i) =>
#pragma warning restore JCC7001
        i.ParentSymbol is not null
            ? [string.Concat(i.ParentSymbol, ".", i.Name), i.Name]
            : [i.Name];

    private static string FormatCallEdge(CallEdge e) =>
        string.Concat(e.CallerSymbol, "→", e.CalleeSymbol);

    private static string ExtractFileName(string path)
    {
        var span = path.AsSpan();
        var lastSep = span.LastIndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return lastSep >= 0 ? span[(lastSep + 1)..].ToString() : path;
    }

    private static double Percentile(List<double> sorted, int percentile)
    {
        if (sorted.Count == 0) return 0;
        var index = (percentile / 100.0) * (sorted.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
    }
}
