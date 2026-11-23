namespace JoinCode.CodeIndex.Tests;/// <summary>
/// FindLastIndexByStartLine 二分查找防御测试 — 变异挂死免疫
/// <para>背景(#102 CodeIndex 超时根因二阶): FindCallerFqn 的二分 `lo = mid + 1` 被变异
/// (+变-/删更新)后, mid==lo 时死循环, testhost CPU 无限自旋 → Stryker 挂死 35min。
/// 拆出纯函数 + SpinWait.Count 守卫(框架内部计数器,单点变异无法破坏),
/// 挂死变异被转换为 InvalidOperationException → 测试失败 → 变异体快速 Killed。</para>
/// </summary>
public sealed class FindLastIndexByStartLineTests {
    private static List<MethodLookupEntry> BuildLookup(params (int start, int end)[] lines)
        => lines.Select((l, i) => new MethodLookupEntry($"M{i}", l.start, l.end)).ToList();

    [Fact]
    public void SingleEntry_MatchInside_ReturnsIndex() {
        var lookup = BuildLookup((1, 10));
        CSharpCallExtractor.FindLastIndexByStartLine(lookup, 5).Should().Be(0);
    }

    [Fact]
    public void MultipleEntries_LineInsideRange_ReturnsLastStartNotAfterLine() {
        var lookup = BuildLookup((1, 5), (6, 10), (11, 20));
        // line=8: 最后一个 StartLine<=8 的是索引1(6,10)
        CSharpCallExtractor.FindLastIndexByStartLine(lookup, 8).Should().Be(1);
    }

    [Fact]
    public void LineBeforeAllEntries_ReturnsMinusOne() {
        var lookup = BuildLookup((10, 20), (30, 40));
        CSharpCallExtractor.FindLastIndexByStartLine(lookup, 5).Should().Be(-1);
    }

    [Fact]
    public void LineAfterAllEntries_ReturnsLastIndex() {
        var lookup = BuildLookup((10, 20), (30, 40));
        CSharpCallExtractor.FindLastIndexByStartLine(lookup, 100).Should().Be(1);
    }

    [Fact]
    public void EmptyLookup_ReturnsMinusOne() {
        CSharpCallExtractor.FindLastIndexByStartLine([], 42).Should().Be(-1);
    }

    [Fact]
    public void LargeLookup_CompletesWithinGuardLimit() {
        // 10万条目: log2(100000)≈17 轮, 远小于 SpinWait 守卫上限 64, 正常路径不误伤
        var lookup = new List<MethodLookupEntry>(100_000);
        for (var i = 0; i < 100_000; i++)
            lookup.Add(new MethodLookupEntry($"M{i}", i * 10, i * 10 + 9));
        CSharpCallExtractor.FindLastIndexByStartLine(lookup, 555_555).Should().Be(55_555);
    }

    [Fact]
    public void SameStartLine_Duplicates_ReturnsLastDuplicate() {
        var lookup = BuildLookup((5, 8), (5, 9), (5, 10));
        CSharpCallExtractor.FindLastIndexByStartLine(lookup, 7).Should().Be(2);
    }
}

/// <summary>
/// SolutionParser.SplitQuotedParts 游标停滞防御测试 — 变异挂死免疫
/// <para>背景: 外层尾部 i++ 被变异(i--)后, 相邻引号场景 i 在 c/c+1 震荡永不前进。
/// 防御: 每轮检测游标停滞(i <= 轮首), SpinWait.Count 超过输入长度即抛(正常路径每轮至少前进 1)。</para>
/// </summary>
public sealed class SplitQuotedPartsStallGuardTests {
    [Fact]
    public void AdjacentEmptyQuotes_CompletesWithoutStall() {
        // 旧代码变异后的震荡触发形态: 连续空引号
        var parts = SolutionParser.SplitQuotedParts("\"\"\"\"".AsSpan());
        parts.Should().BeEmpty();
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty() {
        SolutionParser.SplitQuotedParts(ReadOnlySpan<char>.Empty).Should().BeEmpty();
    }

    [Fact]
    public void OnlyQuotes_ManyPairs_Completes() {
        // 100 对空引号: 每轮游标至少 +2, 停滞检测永不触发
        var input = new string('"', 200);
        SolutionParser.SplitQuotedParts(input.AsSpan()).Should().BeEmpty();
    }

    [Fact]
    public void MixedContent_LargeInput_Completes() {
        // 长输入正常路径: 防御上限 = 输入长度 + 8, 正常每轮前进 ≥1, 不会误伤
        var items = string.Join(", ", Enumerable.Range(0, 500).Select(i => $"\"item{i}\""));
        var parts = SolutionParser.SplitQuotedParts(items.AsSpan());
        parts.Should().HaveCount(500);
        parts[499].Should().Be("item499");
    }
}
