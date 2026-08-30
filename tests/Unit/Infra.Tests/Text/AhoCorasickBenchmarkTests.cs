namespace JoinCode.Infra.Tests.Text;

/// <summary>
/// AC 自动机基准测试 — 量化 AC 自动机 vs foreach Contains 的性能差异。
/// 注意: Debug 模式下 AC 自动机有 Dictionary 开销,Release 模式下 JIT 优化后差距更明显。
/// AC 自动机的核心优势在于: 模式多(500+) + 长文本 + 无公共前缀时,一次扫描替代 N 次扫描。
/// </summary>
public class AhoCorasickBenchmarkTests
{
    private const int Iterations = 1000;

    [Fact]
    public void ContainsAny_50Patterns_AcComparableToContains()
    {
        var patterns = Enumerable.Range(0, 50).Select(i => $"pattern_{i}").ToList();
        var text = new string('x', 5000) + "pattern_25" + new string('x', 5000);

        var ac = AhoCorasick.Create(patterns, ignoreCase: true);

        var acMs = Time(() => ac.ContainsAny(text.AsSpan()));
        var containsMs = Time(() =>
        {
            foreach (var p in patterns)
            {
                if (text.Contains(p, StringComparison.OrdinalIgnoreCase))
                    break;
            }
        });

        acMs.Should().BeLessThanOrEqualTo(containsMs * 3);
    }

    [Fact]
    public void ContainsAny_500Patterns_NoMatch_AcFaster()
    {
        var patterns = Enumerable.Range(0, 500).Select(i => $"kw{i}_unique").ToList();
        var text = new string('x', 50000);

        var ac = AhoCorasick.Create(patterns, ignoreCase: true);

        var acMs = Time(() => ac.ContainsAny(text.AsSpan()));
        var containsMs = Time(() =>
        {
            foreach (var p in patterns)
            {
                if (text.Contains(p, StringComparison.OrdinalIgnoreCase))
                    break;
            }
        });

        acMs.Should().BeLessThanOrEqualTo(containsMs);
    }

    [Fact]
    public void ContainsAny_200Patterns_NoMatch_AcFaster()
    {
        var patterns = Enumerable.Range(0, 200).Select(i => $"keyword_{i}").ToList();
        var text = new string('x', 40000);

        var ac = AhoCorasick.Create(patterns, ignoreCase: true);

        var acMs = Time(() => ac.ContainsAny(text.AsSpan()));
        var containsMs = Time(() =>
        {
            foreach (var p in patterns)
            {
                if (text.Contains(p, StringComparison.OrdinalIgnoreCase))
                    break;
            }
        });

        acMs.Should().BeLessThanOrEqualTo(containsMs);
    }

    [Fact]
    public void FindAll_100Patterns_AcComparableToContains()
    {
        var patterns = Enumerable.Range(0, 100).Select(i => $"tag_{i}").ToList();
        var sb = new StringBuilder();
        for (var i = 0; i < 100; i++)
            sb.Append("tag_").Append(i).Append(' ');
        var text = sb.ToString();

        var ac = AhoCorasick.Create(patterns, ignoreCase: true);

        var acMs = Time(() => ac.FindAll(text.AsSpan()));
        var containsMs = Time(() =>
        {
            var results = new List<string>();
            foreach (var p in patterns)
            {
                if (text.Contains(p, StringComparison.OrdinalIgnoreCase))
                    results.Add(p);
            }
        });

        acMs.Should().BeLessThanOrEqualTo(containsMs * 3);
    }

    [Fact]
    public void DualBuffer_SwapPatterns_OverheadAcceptable()
    {
        var db = DualBufferAhoCorasick.Create(Enumerable.Range(0, 50).Select(i => $"init_{i}"));
        var newPatterns = Enumerable.Range(0, 50).Select(i => $"new_{i}").ToList();

        var swapMs = Time(() =>
            db.SwapPatterns(newPatterns.Select(static p => new KeyValuePair<string, string>(p, p))));

        swapMs.Should().BeLessThanOrEqualTo(500);
    }

    private static long Time(Action action)
    {
        action();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
            action!();
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }
}
