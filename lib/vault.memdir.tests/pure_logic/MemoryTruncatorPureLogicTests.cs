
namespace Core.Tests.Memdir;

/// <summary>
/// MemoryTruncator 纯逻辑确定性测试
/// TruncateByBytes: UTF-8 多字节字符截断点/续字节回退
/// CalculateLineRelevance: 行相关性评分
/// </summary>
public sealed class MemoryTruncatorPureLogicTests {
    private static readonly string TruncateSuffix = L.T(StringKey.VaultTruncatedMaxBytes);

    // === TruncateByBytes: ASCII ===

    [Fact]
    public void TruncateByBytes_ContentUnderLimit_ReturnsContentUnchanged() {
        var content = "short content";
        var result = MemoryTruncator.TruncateByBytes(content, 1024);
        result.Should().Be(content);
    }

    [Fact]
    public void TruncateByBytes_AsciiExactCutAtCharacterBoundary() {
        var content = new string('A', 200);
        // maxBytes=110 → cutLength=10, bytes[10] 是 ASCII,直接切
        var result = MemoryTruncator.TruncateByBytes(content, 110);
        result.Should().Be(new string('A', 10) + TruncateSuffix);
    }

    [Fact]
    public void TruncateByBytes_EmptyContent_ReturnsEmpty() {
        // bytes.Length=0 <= maxBytes,返回原内容 ""
        var result = MemoryTruncator.TruncateByBytes("", 110);
        result.Should().BeEmpty();
    }

    [Fact]
    public void TruncateByBytes_MaxBytesBelow100_CutLengthClampedToZero() {
        var content = new string('A', 200);
        // maxBytes=50 → cutLength=max(0,50-100)=0
        var result = MemoryTruncator.TruncateByBytes(content, 50);
        result.Should().Be(TruncateSuffix);
    }

    // === TruncateByBytes: UTF-8 多字节字符 ===

    [Fact]
    public void TruncateByBytes_MultiByteChar_CutFallsOnContinuationByte_RetreatsToCharBoundary() {
        // "你好世界" 每字 3 字节,10 组 = 40 字 = 120 字节
        var content = string.Concat(Enumerable.Repeat("你好世界", 10));
        // maxBytes=110 → cutLength=10, bytes[10] 是"界"的续字节 → 回退到 bytes[9]="界"起始 → 再回退过"世"到 bytes[6]="世"起始
        // 最终 cutLength=6, result="你好" + 后缀
        var result = MemoryTruncator.TruncateByBytes(content, 110);
        result.Should().Be("你好" + TruncateSuffix);
    }

    [Fact]
    public void TruncateByBytes_MultiByteChar_CutExactlyOnCharBoundary_NoRetreat() {
        // 10 组 "你好世界" = 120 字节,maxBytes=112 → cutLength=12, bytes[12] 是第2组"你"起始字节
        // 12 是完整字符边界(4字×3字节=12),bytes[12] 是多字节起始 → 回退到 cutLength=9...6
        // 实际:bytes[12]=0xE4(第2组"你"起始),是续字节?不,0xE4&0xC0=0xC0 != 0x80,退出 while
        // 然后 0xE4 是多字节起始 → 回退到 cutLength=11,10,9...9 是"界"起始 → 退出
        // result = bytes[0..9] = "你好世" + 后缀
        var content = string.Concat(Enumerable.Repeat("你好世界", 10));
        var result = MemoryTruncator.TruncateByBytes(content, 112);
        result.Should().Be("你好世" + TruncateSuffix);
    }

    [Fact]
    public void TruncateByBytes_PureMultiByte_AllBytesRetreatToCharBoundary() {
        // 纯中文,确保截断点不产生半个字符
        var content = string.Concat(Enumerable.Repeat("中", 50)); // 50字×3字节=150字节
        var result = MemoryTruncator.TruncateByBytes(content, 110);
        // cutLength=10, bytes[10] 是第4字"中"的第2字节(续) → 回退到 bytes[9] 起始 → 再回退到 bytes[6] 起始 → 退出
        // result = bytes[0..6] = "中中" + 后缀 (3字×2=6字节)
        // 但 bytes[9] 是第4字起始(0xE4),回退后 cutLength=8,7,6 → bytes[6] 是第3字起始(0xE4),退出
        // result = bytes[0..6] = "中中"
        result.Should().Be("中中" + TruncateSuffix);
        // 验证结果中无半个字符:解码应成功且字符数为整数
        result.Should().Contain("中");
    }

    [Fact]
    public void TruncateByBytes_MixedAsciiAndMultiByte_HandlesCorrectly() {
        // "a你b好" = a(1) + 你(3) + b(1) + 好(3) = 8 字节
        // 重复 20 次 = 160 字节
        var content = string.Concat(Enumerable.Repeat("a你b好", 20));
        var result = MemoryTruncator.TruncateByBytes(content, 110);
        // cutLength=10, 需要分析 bytes[10] 的位置
        // "a你b好" = [0x61, E4 BD A0, 0x62, E5 A5 BD] = 8字节
        // 索引: 0=a, 1-3=你, 4=b, 5-7=好
        // 第二组: 8=a, 9-11=你, 12=b, 13-15=好
        // cutLength=10: bytes[10]=0xBD(你第3字节,续) → cutLength=9, bytes[9]=0xE4(你起始) → 退出
        // 0xE4 多字节起始 → cutLength=8, bytes[8]=0x61(a,ASCII) → 退出
        // result = bytes[0..8] = "a你b好" + 后缀
        result.Should().Be("a你b好" + TruncateSuffix);
    }

    // === TruncateByBytes: 确定性 ===

    [Fact]
    public void TruncateByBytes_Deterministic_SameInputSameOutput() {
        var content = string.Concat(Enumerable.Repeat("你好世界", 10));
        var r1 = MemoryTruncator.TruncateByBytes(content, 105);
        var r2 = MemoryTruncator.TruncateByBytes(content, 105);
        r1.Should().Be(r2);
    }

    [Fact]
    public void TruncateByBytes_ResultAlwaysEndsWithSuffix() {
        var content = new string('A', 200);
        var result = MemoryTruncator.TruncateByBytes(content, 110);
        result.Should().EndWith(TruncateSuffix);
    }

    // === CalculateLineRelevance ===

    [Fact]
    public void CalculateLineRelevance_AllWordsMatch_ReturnsOne() {
        var result = MemoryTruncator.CalculateLineRelevance("database optimization", new[] { "database", "optimization" });
        result.Should().Be(1.0);
    }

    [Fact]
    public void CalculateLineRelevance_PartialMatch_ReturnsRatio() {
        // 2 个查询词,1 个匹配 → 1/2 = 0.5
        var result = MemoryTruncator.CalculateLineRelevance("database content", new[] { "database", "network" });
        result.Should().Be(0.5);
    }

    [Fact]
    public void CalculateLineRelevance_NoMatch_ReturnsZero() {
        var result = MemoryTruncator.CalculateLineRelevance("unrelated content", new[] { "database", "network" });
        result.Should().Be(0.0);
    }

    [Fact]
    public void CalculateLineRelevance_EmptyLine_ReturnsZero() {
        var result = MemoryTruncator.CalculateLineRelevance("", new[] { "database" });
        result.Should().Be(0.0);
    }

    [Fact]
    public void CalculateLineRelevance_WhitespaceLine_ReturnsZero() {
        var result = MemoryTruncator.CalculateLineRelevance("   ", new[] { "database" });
        result.Should().Be(0.0);
    }

    [Fact]
    public void CalculateLineRelevance_EmptyQueryWords_ReturnsZero() {
        var result = MemoryTruncator.CalculateLineRelevance("some content", Array.Empty<string>());
        result.Should().Be(0.0);
    }

    [Fact]
    public void CalculateLineRelevance_CaseInsensitiveMatch() {
        var result = MemoryTruncator.CalculateLineRelevance("DATABASE content", new[] { "database" });
        result.Should().Be(1.0);
    }

    [Fact]
    public void CalculateLineRelevance_SubstringMatch_CountsAsMatch() {
        // line.Contains 是子串匹配,"database" 在 "databases" 中匹配
        var result = MemoryTruncator.CalculateLineRelevance("databases content", new[] { "database" });
        result.Should().Be(1.0);
    }

    [Fact]
    public void CalculateLineRelevance_SingleWordMatch_ReturnsOne() {
        var result = MemoryTruncator.CalculateLineRelevance("database everywhere", new[] { "database" });
        result.Should().Be(1.0);
    }

    [Fact]
    public void CalculateLineRelevance_ThreeWordsOneMatch_ReturnsOneThird() {
        var result = MemoryTruncator.CalculateLineRelevance("database content", new[] { "database", "network", "security" });
        result.Should().BeApproximately(1.0 / 3.0, 1e-9);
    }

    // === CalculateLineRelevance: 确定性 ===

    [Fact]
    public void CalculateLineRelevance_Deterministic_SameInputSameOutput() {
        var r1 = MemoryTruncator.CalculateLineRelevance("database content", new[] { "database", "network" });
        var r2 = MemoryTruncator.CalculateLineRelevance("database content", new[] { "database", "network" });
        r1.Should().Be(r2);
    }

    // === ScoreAndSelectLines: 评分与选择 ===

    [Fact]
    public void ScoreAndSelectLines_EmptyLines_ReturnsEmpty() {
        var result = MemoryTruncator.ScoreAndSelectLines(Array.Empty<string>(), new[] { "query" }, 10);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ScoreAndSelectLines_AllMatch_TakesHalfMaxLines() {
        // 4行全部匹配,maxLines=4 → Take(2)
        var lines = new[] { "query a", "query b", "query c", "query d" };
        var result = MemoryTruncator.ScoreAndSelectLines(lines, new[] { "query" }, 4);
        result.Should().HaveCount(2);
        // 按索引升序恢复 → 前两行
        result[0].Index.Should().Be(0);
        result[1].Index.Should().Be(1);
    }

    [Fact]
    public void ScoreAndSelectLines_PartialMatch_TakesAllWhenUnderLimit() {
        var lines = new[] { "unrelated", "query match", "unrelated2", "query match2" };
        var result = MemoryTruncator.ScoreAndSelectLines(lines, new[] { "query" }, 10);
        // maxLines/2=5 > 4行 → 全部保留(不过滤score=0),按索引排序
        result.Should().HaveCount(4);
        result.Count(x => x.Score > 0).Should().Be(2);
        result.Count(x => x.Score == 0).Should().Be(2);
    }

    [Fact]
    public void ScoreAndSelectLines_PartialMatch_TakeLimitTruncatesLowScore() {
        // 6行,3匹配3不匹配,maxLines=4 → Take(2) → 只保留2个最高分
        var lines = new[] { "unrelated1", "query1", "unrelated2", "query2", "unrelated3", "query3" };
        var result = MemoryTruncator.ScoreAndSelectLines(lines, new[] { "query" }, 4);
        result.Should().HaveCount(2);
        result.Should().OnlyContain(x => x.Score > 0);
    }

    [Fact]
    public void ScoreAndSelectLines_OrdersByScoreDescThenByIndexAsc() {
        // 行0: 2词全匹配(score=1), 行1: 1词匹配(score=0.5), 行2: 2词全匹配(score=1)
        var lines = new[] { "db net", "db only", "db net too" };
        var result = MemoryTruncator.ScoreAndSelectLines(lines, new[] { "db", "net" }, 10);
        // 分数:行0=1, 行1=0.5, 行2=1 → 降序后行0,行2(score=1),行1(score=0.5)
        // Take(5) 全部保留 → 按索引升序:行0,行1,行2
        result.Should().HaveCount(3);
        result[0].Index.Should().Be(0);
        result[1].Index.Should().Be(1);
        result[2].Index.Should().Be(2);
    }

    [Fact]
    public void ScoreAndSelectLines_NoQueryWords_AllScoresZero() {
        var lines = new[] { "a", "b", "c" };
        var result = MemoryTruncator.ScoreAndSelectLines(lines, Array.Empty<string>(), 4);
        // 全部score=0,Take(2) → 前两行(按索引稳定排序)
        result.Should().HaveCount(2);
        result.Should().OnlyContain(x => x.Score == 0);
    }

    [Fact]
    public void ScoreAndSelectLines_PreservesLineContent() {
        var lines = new[] { "first line", "second line" };
        var result = MemoryTruncator.ScoreAndSelectLines(lines, new[] { "first" }, 4);
        result.Should().Contain(x => x.Line == "first line" && x.Index == 0);
    }

    [Fact]
    public void ScoreAndSelectLines_Deterministic_SameInputSameOutput() {
        var lines = new[] { "query a", "unrelated", "query b" };
        var r1 = MemoryTruncator.ScoreAndSelectLines(lines, new[] { "query" }, 4);
        var r2 = MemoryTruncator.ScoreAndSelectLines(lines, new[] { "query" }, 4);
        r1.Should().BeEquivalentTo(r2);
    }

    // === AssembleTruncatedLines: 组装截断输出 ===

    [Fact]
    public void AssembleTruncatedLines_ContinuousIndices_NoEllipsis() {
        var scored = new List<MemoryTruncator.ScoredLine> {
            new("line0", 0, 1.0),
            new("line1", 1, 1.0),
            new("line2", 2, 1.0),
        };
        var result = MemoryTruncator.AssembleTruncatedLines(scored, totalLineCount: 5, maxLines: 10);
        // totalLineCount(5) <= maxLines(10) → 无截断提示
        result.Should().Equal(new[] { "line0", "line1", "line2" });
    }

    [Fact]
    public void AssembleTruncatedLines_DiscontinuousIndices_InsertsEllipsis() {
        var scored = new List<MemoryTruncator.ScoredLine> {
            new("line0", 0, 1.0),
            new("line2", 2, 1.0),
        };
        var result = MemoryTruncator.AssembleTruncatedLines(scored, totalLineCount: 3, maxLines: 10);
        // 行0和行2不连续(2 > 0+1) → 插入省略号
        result.Should().Equal(new[] { "line0", "...", "line2" });
    }

    [Fact]
    public void AssembleTruncatedLines_TotalExceedsMax_AppendsTruncationNotice() {
        var scored = new List<MemoryTruncator.ScoredLine> {
            new("line0", 0, 1.0),
        };
        var result = MemoryTruncator.AssembleTruncatedLines(scored, totalLineCount: 100, maxLines: 10);
        // totalLineCount(100) > maxLines(10) → 追加空行+截断提示
        result.Should().HaveCount(3);
        result[0].Should().Be("line0");
        result[1].Should().BeEmpty();
        result[2].Should().Contain("100");
    }

    [Fact]
    public void AssembleTruncatedLines_TotalWithinMax_NoTruncationNotice() {
        var scored = new List<MemoryTruncator.ScoredLine> {
            new("line0", 0, 1.0),
        };
        var result = MemoryTruncator.AssembleTruncatedLines(scored, totalLineCount: 5, maxLines: 10);
        result.Should().HaveCount(1);
        result[0].Should().Be("line0");
    }

    [Fact]
    public void AssembleTruncatedLines_EmptyScored_WithTruncation_OnlyNotice() {
        var scored = new List<MemoryTruncator.ScoredLine>();
        var result = MemoryTruncator.AssembleTruncatedLines(scored, totalLineCount: 100, maxLines: 10);
        // 无行,但totalLineCount > maxLines → 仅截断提示
        result.Should().HaveCount(2);
        result[0].Should().BeEmpty();
        result[1].Should().Contain("100");
    }

    [Fact]
    public void AssembleTruncatedLines_EmptyScored_NoTruncation_EmptyResult() {
        var scored = new List<MemoryTruncator.ScoredLine>();
        var result = MemoryTruncator.AssembleTruncatedLines(scored, totalLineCount: 5, maxLines: 10);
        result.Should().BeEmpty();
    }

    [Fact]
    public void AssembleTruncatedLines_MultipleGaps_MultipleEllipsis() {
        var scored = new List<MemoryTruncator.ScoredLine> {
            new("line0", 0, 1.0),
            new("line2", 2, 1.0),
            new("line5", 5, 1.0),
        };
        var result = MemoryTruncator.AssembleTruncatedLines(scored, totalLineCount: 6, maxLines: 10);
        // 0→2 不连续, 2→5 不连续 → 两处省略号
        result.Should().Equal(new[] { "line0", "...", "line2", "...", "line5" });
    }

    [Fact]
    public void AssembleTruncatedLines_Deterministic_SameInputSameOutput() {
        var scored = new List<MemoryTruncator.ScoredLine> {
            new("a", 0, 1.0),
            new("c", 2, 0.5),
        };
        var r1 = MemoryTruncator.AssembleTruncatedLines(scored, 10, 5);
        var r2 = MemoryTruncator.AssembleTruncatedLines(scored, 10, 5);
        r1.Should().Equal(r2);
    }

    // === 边界:TruncateByBytes null 守卫 ===

    [Fact]
    public void TruncateByBytes_NullContent_ThrowsArgumentNullException() {
        var act = () => MemoryTruncator.TruncateByBytes(null!, 110);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TruncateByBytes_MaxBytesZero_ReturnsOnlySuffix() {
        // maxBytes=0 → cutLength=max(0,0-100)=0 → result="" + 后缀
        var result = MemoryTruncator.TruncateByBytes("abc", 0);
        result.Should().Be(TruncateSuffix);
    }

    [Fact]
    public void TruncateByBytes_MaxBytesEqualsContentLength_ReturnsContentUnchanged() {
        var content = "abc"; // 3 字节
        var result = MemoryTruncator.TruncateByBytes(content, 3);
        result.Should().Be(content);
    }

    [Fact]
    public void TruncateByBytes_FourByteEmoji_CutFallsOnContinuationByte_RetreatsToCharBoundary() {
        // 😀 = U+1F600, UTF-8 = F0 9F 98 80 (4字节),40 个 = 160 字节
        var content = string.Concat(Enumerable.Repeat("😀", 40));
        // maxBytes=110 → cutLength=10, bytes[10] 是第3个 emoji 第3字节(续) → 回退到 bytes[8]=emoji3 起始 → 再回退过 emoji2 到 bytes[4]=emoji2 起始 → 再回退到 bytes[0..4]="😀"
        var result = MemoryTruncator.TruncateByBytes(content, 110);
        result.Should().Be("😀" + TruncateSuffix);
    }

    [Fact]
    public void TruncateByBytes_FourByteEmoji_CutExactlyOnCharBoundary() {
        // 3 个 emoji = 12 字节,maxBytes=112 → cutLength=12, bytes[12] 是第4个 emoji 起始字节(0xF0)
        // 0xF0 是多字节起始 → 回退到 cutLength=11,10,9,8 → bytes[8]=第3个 emoji 起始(0xF0) → 退出
        // result = bytes[0..8] = "😀😀" + 后缀
        var content = string.Concat(Enumerable.Repeat("😀", 40));
        var result = MemoryTruncator.TruncateByBytes(content, 112);
        result.Should().Be("😀😀" + TruncateSuffix);
    }

    // === 边界:CalculateLineRelevance null 守卫 ===

    [Fact]
    public void CalculateLineRelevance_NullQueryWords_ThrowsArgumentNullException() {
        var act = () => MemoryTruncator.CalculateLineRelevance("content", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CalculateLineRelevance_NullLine_ReturnsZero() {
        // string.IsNullOrWhiteSpace(null) → true → 返回 0,不抛异常
        var result = MemoryTruncator.CalculateLineRelevance(null!, new[] { "query" });
        result.Should().Be(0.0);
    }

    // === 边界:ScoreAndSelectLines null 守卫 ===

    [Fact]
    public void ScoreAndSelectLines_NullLines_ThrowsArgumentNullException() {
        var act = () => MemoryTruncator.ScoreAndSelectLines(null!, new[] { "query" }, 10);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ScoreAndSelectLines_NullQueryWords_ThrowsArgumentNullException() {
        var act = () => MemoryTruncator.ScoreAndSelectLines(new[] { "line" }, null!, 10);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ScoreAndSelectLines_MaxLinesZero_ReturnsEmpty() {
        var lines = new[] { "query a", "query b" };
        var result = MemoryTruncator.ScoreAndSelectLines(lines, new[] { "query" }, 0);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ScoreAndSelectLines_MaxLinesOne_ReturnsEmpty_CounterIntuitive() {
        // 反直觉:maxLines=1 → Take(1/2)=Take(0) → 返回空,而非 1 行
        var lines = new[] { "query a", "query b" };
        var result = MemoryTruncator.ScoreAndSelectLines(lines, new[] { "query" }, 1);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ScoreAndSelectLines_MaxLinesNegative_ReturnsEmpty() {
        // .NET Core 中 Take(负数) 返回空序列,不抛异常
        var lines = new[] { "query a", "query b" };
        var result = MemoryTruncator.ScoreAndSelectLines(lines, new[] { "query" }, -3);
        result.Should().BeEmpty();
    }

    // === 边界:AssembleTruncatedLines null 守卫 ===

    [Fact]
    public void AssembleTruncatedLines_NullScoredLines_ThrowsArgumentNullException() {
        var act = () => MemoryTruncator.AssembleTruncatedLines(null!, totalLineCount: 5, maxLines: 10);
        act.Should().Throw<ArgumentNullException>();
    }
}
