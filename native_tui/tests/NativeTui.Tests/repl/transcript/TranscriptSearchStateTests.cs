namespace NativeTui.Tests.Repl.Transcript;

/// <summary>
/// TranscriptSearchState 单元测试 — 对齐基准 REPL.tsx 4271-4288 + TranscriptSearchBar
///（368-439）：增量查询每键重扫（OrdinalIgnoreCase 子串 + 同行不重叠多命中）、
/// n/N 循环导航、cur/count 徽章数据、Enter 提交保留查询（less 语义）、Esc 撤销清空、
/// '/' 预填上次查询、FocusNearest 就近定位与回绕。全部纯逻辑（注入假行提供器）。
/// </summary>
public sealed class TranscriptSearchStateTests
{
    /// <summary>构造搜索状态（固定行源）。</summary>
    private static TranscriptSearchState New(params string[] lines) => new(() => lines);

    /// <summary>增量输入 — 追加查询并每键重扫。</summary>
    [Fact]
    public void Type_AppendsQuery_AndRescansPerKeystroke()
    {
        var search = New("alpha beta", "gamma");
        search.Type("a");
        Assert.Equal("a", search.Query);
        var oneChar = search.Count;
        search.Type("l");
        Assert.Equal("al", search.Query);
        Assert.NotEqual(oneChar, search.Count);
    }

    /// <summary>匹配区间 — 行序 + 列序；同行多命中；大小写不敏感（基准 toLowerCase + indexOf）。</summary>
    [Fact]
    public void Matches_CaseInsensitive_MultiPerLine()
    {
        var search = New("Hello World");
        search.Type("HELLO");
        var match = Assert.Single(search.Matches);
        Assert.Equal(new TranscriptMatch(0, 0, 5), match);
    }

    /// <summary>同行多命中 — 不重叠推进（"ababab" 查 "ab" → 列 0/2/4）。</summary>
    [Fact]
    public void Matches_SameLine_NonOverlapping()
    {
        var search = New("ababab");
        search.Type("ab");
        Assert.Equal(
        [
            new TranscriptMatch(0, 0, 2),
            new TranscriptMatch(0, 2, 2),
            new TranscriptMatch(0, 4, 2),
        ], search.Matches);
    }

    /// <summary>空查询 — 无匹配、徽章 0。</summary>
    [Fact]
    public void Matches_EmptyQuery_None()
    {
        var search = New("alpha");
        search.OpenBar();
        Assert.Equal(0, search.Count);
        Assert.Null(search.Current);
        Assert.Equal(0, search.CurrentOrdinal);
    }

    /// <summary>查询跨多字符 — 匹配长度等于查询长度。</summary>
    [Fact]
    public void Matches_LengthEqualsQuery()
    {
        var search = New("xx alphaxx");
        search.Type("alpha");
        var match = Assert.Single(search.Matches);
        Assert.Equal(new TranscriptMatch(0, 3, 5), match);
    }

    /// <summary>退格 — 删除末字符并重扫。</summary>
    [Fact]
    public void Backspace_RemovesLastChar_AndRescans()
    {
        var search = New("alpha", "beta");
        search.Type("al");
        Assert.Equal(1, search.Count);
        search.Backspace();
        Assert.Equal("a", search.Query);
        Assert.Equal(3, search.Count);
    }

    /// <summary>空查询退格 — 无害（查询与匹配均空）。</summary>
    [Fact]
    public void Backspace_EmptyQuery_Noop()
    {
        var search = New("alpha");
        search.Backspace();
        Assert.Equal(string.Empty, search.Query);
        Assert.Equal(0, search.Count);
    }

    /// <summary>提交（Return）— 关闭栏但查询与匹配保留（n/N 继续工作 — less 语义）。</summary>
    [Fact]
    public void CommitBar_KeepsQueryAndMatches()
    {
        var search = New("alpha", "beta");
        search.Type("a");
        var count = search.Count;
        search.CommitBar();
        Assert.False(search.BarOpen);
        Assert.Equal("a", search.Query);
        Assert.Equal(count, search.Count);
    }

    /// <summary>撤销（Esc）— 关闭栏并清空查询与匹配（pre-/ 状态）。</summary>
    [Fact]
    public void Cancel_ClearsQueryAndMatches()
    {
        var search = New("alpha");
        search.Type("a");
        search.Cancel();
        Assert.False(search.BarOpen);
        Assert.Equal(string.Empty, search.Query);
        Assert.Equal(0, search.Count);
    }

    /// <summary>重开搜索栏 — 预填上次查询（基准 initialQuery 种子 — less shows last pattern）。</summary>
    [Fact]
    public void OpenBar_RefillsLastQuery()
    {
        var search = New("alpha");
        search.Type("al");
        search.CommitBar();
        search.OpenBar();
        Assert.True(search.BarOpen);
        Assert.Equal("al", search.Query);
        Assert.Equal(1, search.Count);
    }

    /// <summary>n 下一匹配 — 循环（越过末个回绕首个）。</summary>
    [Fact]
    public void Next_WrapsAround()
    {
        var search = New("a one", "a two", "a three");
        search.Type("a");
        Assert.Equal(3, search.Count);
        Assert.Equal(1, search.CurrentOrdinal);
        search.Next();
        Assert.Equal(2, search.CurrentOrdinal);
        search.Next();
        Assert.Equal(3, search.CurrentOrdinal);
        search.Next();
        Assert.Equal(1, search.CurrentOrdinal);
    }

    /// <summary>N 上一匹配 — 循环（越过首个回绕末个）。</summary>
    [Fact]
    public void Prev_WrapsAround()
    {
        var search = New("a one", "a two", "a three");
        search.Type("a");
        search.Prev();
        Assert.Equal(3, search.CurrentOrdinal);
        search.Prev();
        Assert.Equal(2, search.CurrentOrdinal);
    }

    /// <summary>无匹配时 n/N — 不动返回 false。</summary>
    [Fact]
    public void NextPrev_NoMatches_ReturnFalse()
    {
        var search = New("alpha");
        Assert.False(search.Next());
        Assert.False(search.Prev());
        Assert.Equal(0, search.CurrentIndex);
    }

    /// <summary>当前匹配 — CurrentIndex 指向的区间。</summary>
    [Fact]
    public void Current_FollowsCurrentIndex()
    {
        var search = New("a one", "a two");
        search.Type("a");
        Assert.NotNull(search.Current);
        Assert.Equal(new TranscriptMatch(0, 0, 1), search.Current!);
        search.Next();
        Assert.NotNull(search.Current);
        Assert.Equal(new TranscriptMatch(1, 0, 1), search.Current!);
    }

    /// <summary>FocusNearest — 行号 >= 锚的第一个匹配。</summary>
    [Fact]
    public void FocusNearest_PicksFirstAtOrAfterAnchor()
    {
        var search = New("hit", "miss", "miss", "hit", "miss", "hit");
        search.Type("hit");
        Assert.True(search.FocusNearest(2));
        Assert.NotNull(search.Current);
        Assert.Equal(new TranscriptMatch(3, 0, 3), search.Current!);
    }

    /// <summary>FocusNearest — 锚之上无匹配回绕首个。</summary>
    [Fact]
    public void FocusNearest_WrapsToFirst()
    {
        var search = New("hit", "miss", "hit");
        search.Type("hit");
        Assert.True(search.FocusNearest(10));
        Assert.Equal(0, search.CurrentIndex);
    }

    /// <summary>FocusNearest — 无匹配返回 false 不动。</summary>
    [Fact]
    public void FocusNearest_NoMatches_ReturnFalse()
    {
        var search = New("alpha");
        Assert.False(search.FocusNearest(0));
    }

    /// <summary>重置（退出 transcript）— 全清。</summary>
    [Fact]
    public void Reset_ClearsAll()
    {
        var search = New("alpha");
        search.Type("a");
        search.Reset();
        Assert.False(search.BarOpen);
        Assert.Equal(string.Empty, search.Query);
        Assert.Equal(0, search.Count);
    }

    /// <summary>行源委托 — 每次重扫实时读取（Controller 快照行绑定语义的底层支撑）。</summary>
    [Fact]
    public void LinesProvider_ReadsPerRescan()
    {
        var lines = new List<string> { "alpha" };
        var search = new TranscriptSearchState(() => lines);
        search.Type("a");
        Assert.Equal(2, search.Count);  // "alpha" 含两处 a（idx0/idx4）
        lines.Add("a second");
        search.Backspace();
        search.Type("a");
        Assert.Equal(3, search.Count);  // alpha 两处 + "a second" 一处
    }
}
