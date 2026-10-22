namespace NativeTui.Tests.Repl;

/// <summary>
/// FuzzyMatcher 打分表测试（QuickOpen 对话框域 — 基准 QuickOpenDialog.tsx fuzzy 文件打开语义）：
/// 空查询全匹配 / 子序列不满足零分 / 连续段加分 / 词首加分（含驼峰边界）/ 文件名区加分 /
/// 短路径加分 / 大小写不敏感 / 中文路径（子序匹配 + 乱序拒绝）/ null 参数契约。
/// 断言以关系断言为主（打分常量内部私有 — 锁行为不锁数值）。
/// </summary>
public sealed class DialogFuzzyMatcherTests
{
    /// <summary>空查询 — 恒全匹配基础分（排序退化为路径短优先 + 字母序，消费方语义）。</summary>
    [Fact]
    public void EmptyQuery_MatchesEverythingWithBaseScore()
    {
        Assert.Equal(FuzzyMatcher.EmptyQueryScore, FuzzyMatcher.Match(string.Empty, "src/App.cs"));
        Assert.True(FuzzyMatcher.Match(string.Empty, "任意/中文/路径.json") > 0);
        Assert.True(FuzzyMatcher.Match(string.Empty, string.Empty) > 0);
    }

    /// <summary>空路径 + 非空查询 — 不匹配零分。</summary>
    [Fact]
    public void EmptyPath_NonEmptyQuery_NoMatch()
    {
        Assert.Equal(FuzzyMatcher.ScoreNoMatch, FuzzyMatcher.Match("a", string.Empty));
    }

    /// <summary>子序列不满足 — 零分（字符缺失或顺序颠倒）。</summary>
    [Fact]
    public void NonSubsequence_ScoresZero()
    {
        Assert.Equal(FuzzyMatcher.ScoreNoMatch, FuzzyMatcher.Match("xyz", "src/App.cs"));
        Assert.Equal(FuzzyMatcher.ScoreNoMatch, FuzzyMatcher.Match("ba", "ab"));
        Assert.Equal(FuzzyMatcher.ScoreNoMatch, FuzzyMatcher.Match("csss", "App.css"));
    }

    /// <summary>子序列满足 — 正分（散开字符按序命中）。</summary>
    [Fact]
    public void Subsequence_MatchesPositive()
    {
        Assert.True(FuzzyMatcher.Match("ab", "a-x-b.txt") > 0);
        Assert.True(FuzzyMatcher.Match("acs", "src/App.cs") > 0);
    }

    /// <summary>连续段加分 — 相邻匹配高于散开匹配（同查询同长路径）。</summary>
    [Fact]
    public void ConsecutiveRun_ScoresHigherThanScattered()
    {
        var consecutive = FuzzyMatcher.Match("ab", "xaby");
        var scattered = FuzzyMatcher.Match("ab", "xayb");
        Assert.True(consecutive > scattered, $"连续 {consecutive} 应高于散开 {scattered}");
    }

    /// <summary>词首加分 — 分隔符后首位高于词中字符（basename 区对齐 — 差值仅词首奖励）。</summary>
    [Fact]
    public void WordStart_ScoresHigherThanInnerChar()
    {
        var atWordStart = FuzzyMatcher.Match("b", "foo/bar");
        var innerChar = FuzzyMatcher.Match("r", "foo/bar");
        Assert.True(atWordStart > innerChar, $"词首 {atWordStart} 应高于词中 {innerChar}");
    }

    /// <summary>驼峰边界词首加分 — FooBar 的 B（小→大边界）高于尾部 a（词中）。</summary>
    [Fact]
    public void CamelCaseBoundary_ScoresAsWordStart()
    {
        var camelBoundary = FuzzyMatcher.Match("b", "FooBar");
        var innerChar = FuzzyMatcher.Match("a", "FooBar");
        Assert.True(camelBoundary > innerChar, $"驼峰首 {camelBoundary} 应高于词中 {innerChar}");
    }

    /// <summary>短路径加分 — 同查询短路径高于长路径。</summary>
    [Fact]
    public void ShorterPath_ScoresHigher()
    {
        var shortPath = FuzzyMatcher.Match("a", "a");
        var longPath = FuzzyMatcher.Match("a", "zzzza");
        Assert.True(shortPath > longPath, $"短路径 {shortPath} 应高于长路径 {longPath}");
    }

    /// <summary>文件名区加分 — basename 段内匹配高于等长路径中段匹配。</summary>
    [Fact]
    public void BasenameMatch_ScoresHigherThanDirectorySegment()
    {
        var inBasename = FuzzyMatcher.Match("a", "xxx/yyy/abc");
        var inDirectory = FuzzyMatcher.Match("a", "axx/yyy/zbc");
        Assert.True(inBasename > inDirectory, $"文件名区 {inBasename} 应高于目录段 {inDirectory}");
    }

    /// <summary>大小写不敏感 — 查询与路径大小写折叠比较，同构查询等分。</summary>
    [Fact]
    public void Match_IsCaseInsensitive()
    {
        Assert.True(FuzzyMatcher.Match("ABC", "abc") > 0);
        Assert.Equal(FuzzyMatcher.Match("abc", "abc"), FuzzyMatcher.Match("ABC", "abc"));
        Assert.True(FuzzyMatcher.Match("sh", "ReplShell.cs") > 0);
    }

    /// <summary>中文路径 — 子序列匹配（连续中文段 / 跨目录子序）+ 顺序颠倒拒绝。</summary>
    [Fact]
    public void ChinesePath_SubsequenceMatchesAndRejectsOutOfOrder()
    {
        var path = "src/服务/配置.json";
        Assert.True(FuzzyMatcher.Match("配置", path) > 0);
        Assert.True(FuzzyMatcher.Match("务配", path) > 0);
        Assert.Equal(FuzzyMatcher.ScoreNoMatch, FuzzyMatcher.Match("置配", path));
        Assert.True(FuzzyMatcher.Match("服json", path) > 0);
    }

    /// <summary>中文连续段加分 — 连续命中高于散开命中。</summary>
    [Fact]
    public void ChineseConsecutive_ScoresHigherThanScattered()
    {
        var consecutive = FuzzyMatcher.Match("配置", "x配置y");
        var scattered = FuzzyMatcher.Match("配置", "x配a置y");
        Assert.True(consecutive > scattered, $"连续 {consecutive} 应高于散开 {scattered}");
    }

    /// <summary>null 参数契约 — 抛 ArgumentNullException。</summary>
    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => FuzzyMatcher.Match(null!, "a.cs"));
        Assert.Throws<ArgumentNullException>(() => FuzzyMatcher.Match("a", null!));
    }
}
