namespace NativeTui.Tests.Repl.History;

/// <summary>
/// HistorySearch 单元测试 — 对齐基准 ctrl+r 反向搜索（getTimestampedHistory 语义）：
/// 子串匹配（大小写不敏感）+ 文本去重 + 新→旧排序；查询变化重置到最新匹配；
/// Next 向旧循环；Matches 截断容量。
/// </summary>
public sealed class HistorySearchTests
{
    /// <summary>构造搜索（历史旧→新）。</summary>
    private static HistorySearch Search(string query, params string[] history) =>
        HistorySearch.Begin(query, history);

    /// <summary>基础匹配：查询子串命中，新→旧排序。</summary>
    [Fact]
    public void Begin_SubstringMatch_NewestFirst()
    {
        var search = Search("git", "git status", "hello world", "git commit", "git push");
        Assert.Equal(["git push", "git commit", "git status"], search.Matches);
    }

    /// <summary>大小写不敏感匹配。</summary>
    [Fact]
    public void Begin_CaseInsensitive()
    {
        var search = Search("GIT", "git status", "GIT COMMIT");
        Assert.Equal(["GIT COMMIT", "git status"], search.Matches);
    }

    /// <summary>文本去重（同文本只保留最新一条 — 基准 getTimestampedHistory）。</summary>
    [Fact]
    public void Begin_DeduplicatesByText()
    {
        var search = Search("x", "run x", "run x", "run x");
        Assert.Single(search.Matches);
    }

    /// <summary>空查询：全部历史（去重后新→旧）。</summary>
    [Fact]
    public void Begin_EmptyQuery_AllHistory()
    {
        var search = Search("", "a", "b", "a");
        Assert.Equal(["a", "b"], search.Matches);
    }

    /// <summary>当前匹配：MatchIndex 指向的条目。</summary>
    [Fact]
    public void Current_FollowsMatchIndex()
    {
        var search = Search("git", "git status", "git commit");
        Assert.Equal("git commit", search.Current);
        search.Next();
        Assert.Equal("git status", search.Current);
    }

    /// <summary>Next 循环：越过最旧回到最新。</summary>
    [Fact]
    public void Next_WrapsAround()
    {
        var search = Search("git", "git a", "git b", "git c");
        Assert.Equal("git c", search.Current);
        search.Next();
        Assert.Equal("git b", search.Current);
        search.Next();
        Assert.Equal("git a", search.Current);
        search.Next();
        Assert.Equal("git c", search.Current);
    }

    /// <summary>查询变化：重置到最新匹配重新过滤。</summary>
    [Fact]
    public void UpdateQuery_ResetsToNewestMatch()
    {
        var search = Search("git", "git a", "git b", "run gitlab");
        search.Next();
        search.UpdateQuery("gitlab");
        Assert.Equal(["run gitlab"], search.Matches);
        Assert.Equal("run gitlab", search.Current);
    }

    /// <summary>无匹配：Matches 空、Current 空串（调用方保持查询）。</summary>
    [Fact]
    public void NoMatch_EmptyMatches()
    {
        var search = Search("zzz", "git a");
        Assert.Empty(search.Matches);
        Assert.Equal(string.Empty, search.Current);
    }

    /// <summary>容量截断：最多 100 条匹配（对齐基准窗口）。</summary>
    [Fact]
    public void Begin_TruncatesToCapacity()
    {
        var history = Enumerable.Range(0, 150).Select(i => $"cmd-{i}").ToArray();
        var search = Search("cmd", history);
        Assert.Equal(HistorySearch.Capacity, search.Matches.Count);
        Assert.Equal($"cmd-149", search.Matches[0]);
    }

    /// <summary>去重先于容量截断。</summary>
    [Fact]
    public void Begin_DedupBeforeTruncate()
    {
        var history = Enumerable.Range(0, 60).Select(i => $"cmd-{i % 30}").ToArray();
        var search = Search("cmd", history);
        Assert.Equal(30, search.Matches.Count);
    }
}
