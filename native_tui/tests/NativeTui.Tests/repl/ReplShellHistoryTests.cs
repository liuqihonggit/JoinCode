namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell 历史环集成测试 — 对齐基准 useArrowKeyHistory（Chat 上下文 up/down = history:previous/next）：
/// 提交入史；Up 翻旧（首次暂存草稿、光标置行首、到底保持）；Down 翻新（回底恢复草稿）；
/// 种子注入；不回绕；建议激活时 Up/Down 归补全不翻历史。
/// </summary>
public sealed class ReplShellHistoryTests
{
    /// <summary>构造测试 shell（SeedContent 就绪消息区）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell()
    {
        var engine = new FakeReplEngine();
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var shell = new ReplShell(engine, banner, new StringWriter());
        shell.SeedContent();
        return (shell, engine);
    }

    /// <summary>按键注入（功能键）。</summary>
    private static void Press(ReplShell shell, ConsoleKey key) =>
        shell.HandleKey(new ConsoleKeyInfo('\0', key, false, false, false));

    /// <summary>键入文本（经 HandleKey 全链路）。</summary>
    private static void Type(ReplShell shell, string text)
    {
        foreach (var c in text)
        {
            shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.None, false, false, false));
        }
    }

    /// <summary>Ctrl+R 按键注入。</summary>
    private static void PressCtrlR(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('', ConsoleKey.R, true, false, false));

    /// <summary>提交两条并回到空输入态。</summary>
    private static void SubmitTwo(ReplShell shell)
    {
        Type(shell, "first");
        Press(shell, ConsoleKey.Enter);
        Type(shell, "second");
        Press(shell, ConsoleKey.Enter);
    }

    /// <summary>提交入史：Up 翻回最新一条，光标置行首。</summary>
    [Fact]
    public void Submit_RecordsHistory()
    {
        var (shell, _) = CreateShell();
        Type(shell, "hello");
        Press(shell, ConsoleKey.Enter);
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal("hello", shell.Input.Text);
        Assert.Equal(0, shell.Input.CursorCol);
    }

    /// <summary>Up 逐条翻旧；Down 翻新。</summary>
    [Fact]
    public void UpDown_WalksHistory()
    {
        var (shell, _) = CreateShell();
        SubmitTwo(shell);
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal("second", shell.Input.Text);
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal("first", shell.Input.Text);
        Press(shell, ConsoleKey.DownArrow);
        Assert.Equal("second", shell.Input.Text);
    }

    /// <summary>草稿暂存与恢复：翻历史前输入 draft → Up → Down 底部恢复 draft。</summary>
    [Fact]
    public void UpThenDown_RestoresDraft()
    {
        var (shell, _) = CreateShell();
        SubmitTwo(shell);
        Type(shell, "draft");
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal("second", shell.Input.Text);
        Press(shell, ConsoleKey.DownArrow);
        Assert.Equal("draft", shell.Input.Text);
        Assert.Equal("draft".Length, shell.Input.CursorCol);
    }

    /// <summary>Up 到底：越过最旧一条后保持当前输入不动。</summary>
    [Fact]
    public void Up_BeyondOldest_KeepsInput()
    {
        var (shell, _) = CreateShell();
        SubmitTwo(shell);
        Press(shell, ConsoleKey.UpArrow);
        Press(shell, ConsoleKey.UpArrow);
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal("first", shell.Input.Text);
    }

    /// <summary>Down 在输入框态：no-op。</summary>
    [Fact]
    public void Down_AtInput_NoOp()
    {
        var (shell, _) = CreateShell();
        SubmitTwo(shell);
        Press(shell, ConsoleKey.DownArrow);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>空历史 Up：no-op。</summary>
    [Fact]
    public void Up_EmptyHistory_NoOp()
    {
        var (shell, _) = CreateShell();
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>历史种子：宿主注入（transcript 加载）后可翻阅。</summary>
    [Fact]
    public void SeedHistory_ProvidesEntries()
    {
        var (shell, _) = CreateShell();
        shell.SeedHistory(["old-seed", "new-seed"]);
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal("new-seed", shell.Input.Text);
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal("old-seed", shell.Input.Text);
    }

    /// <summary>连续提交相同文本只入史一次（末条去重）— 翻阅仅一条可回。</summary>
    [Fact]
    public void Submit_SameTextTwice_SingleHistoryEntry()
    {
        var (shell, _) = CreateShell();
        Type(shell, "same");
        Press(shell, ConsoleKey.Enter);
        Type(shell, "same");
        Press(shell, ConsoleKey.Enter);
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal("same", shell.Input.Text);
        Press(shell, ConsoleKey.DownArrow);
        Assert.Equal(string.Empty, shell.Input.Text);
        Press(shell, ConsoleKey.UpArrow);
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal("same", shell.Input.Text);
    }

    /// <summary>建议激活时 Up 归补全导航，不翻历史。</summary>
    [Fact]
    public void SuggestionsActive_UpGoesToCompletion()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("1.0.0", "m", "API 按量计费", "/tmp"), new StringWriter(),
            commands: [new("help", "h", [], IsHidden: false), new("hello", "h", [], IsHidden: false)]);
        shell.SeedContent();
        shell.SeedHistory(["hist-entry"]);
        Type(shell, "/he");
        Press(shell, ConsoleKey.UpArrow);
        Assert.True(shell.Suggester?.IsActive);
        Assert.Equal("/he", shell.Input.Text);
        Assert.Equal(1, shell.Suggester?.SelectedIndex);
    }

    /// <summary>翻历史后提交：新输入入史 + 导航态复位。</summary>
    [Fact]
    public void SubmitAfterNavigating_RecordsNewText()
    {
        var (shell, _) = CreateShell();
        SubmitTwo(shell);
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal("second", shell.Input.Text);
        Press(shell, ConsoleKey.Enter);
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal("second", shell.Input.Text);
    }

    /// <summary>Ctrl+R 反向搜索（TASK057-4）：以当前输入为查询，Enter 接受最新匹配并提交。</summary>
    [Fact]
    public void CtrlR_Search_AcceptsNewestMatchOnEnter()
    {
        var (shell, engine) = CreateShell();
        SubmitTwo(shell);
        Type(shell, "fir");
        PressCtrlR(shell);
        Assert.True(shell.IsSearching);
        Press(shell, ConsoleKey.Enter);
        Assert.Equal(["first", "second", "first"], engine.Submits);
        Assert.False(shell.IsSearching);
    }

    /// <summary>Ctrl+R 循环：再按 Ctrl+R 跳到下一个匹配。</summary>
    [Fact]
    public void CtrlR_Repeat_CyclesToNextMatch()
    {
        var (shell, engine) = CreateShell();
        shell.SeedHistory(["git a", "git b", "git c"]);
        Type(shell, "git");
        PressCtrlR(shell);
        PressCtrlR(shell);
        PressCtrlR(shell);
        Press(shell, ConsoleKey.Enter);
        Assert.Equal(["git a"], engine.Submits);
    }

    /// <summary>搜索态查询编辑：输入字符细化查询。</summary>
    [Fact]
    public void CtrlR_Typing_RefinesQuery()
    {
        var (shell, engine) = CreateShell();
        shell.SeedHistory(["git status", "git commit", "run x"]);
        PressCtrlR(shell);
        Type(shell, "git");
        Type(shell, " co");
        Press(shell, ConsoleKey.Enter);
        Assert.Equal(["git commit"], engine.Submits);
    }

    /// <summary>Esc 退出搜索：恢复进入前草稿，不提交。</summary>
    [Fact]
    public void CtrlR_Escape_RestoresDraftWithoutSubmit()
    {
        var (shell, engine) = CreateShell();
        SubmitTwo(shell);
        Type(shell, "draft");
        PressCtrlR(shell);
        Press(shell, ConsoleKey.Escape);
        Assert.False(shell.IsSearching);
        Assert.Equal("draft", shell.Input.Text);
        Assert.Equal(2, engine.Submits.Count);
    }

    /// <summary>搜索态 Up/Down 在匹配间导航（Up = 向旧 = git a）。</summary>
    [Fact]
    public void CtrlR_ArrowsNavigate_Matches()
    {
        var (shell, engine) = CreateShell();
        shell.SeedHistory(["git a", "git b"]);
        Type(shell, "git");
        PressCtrlR(shell);
        Press(shell, ConsoleKey.UpArrow);
        Press(shell, ConsoleKey.Enter);
        Assert.Equal(["git a"], engine.Submits);
    }

    /// <summary>搜索态下补全不触发（查询 "fir" 前无 /，且优先级搜索最高）。</summary>
    [Fact]
    public void CtrlR_SearchSuppressesSuggestions()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("1.0.0", "m", "API 按量计费", "/tmp"), new StringWriter(),
            commands: [new("first-cmd", "d", [], IsHidden: false)]);
        shell.SeedContent();
        shell.SeedHistory(["first-cmd ran"]);
        Type(shell, "fir");
        PressCtrlR(shell);
        Assert.False(shell.Suggester?.IsActive);
    }

    /// <summary>空历史 Ctrl+R：进入搜索态但无匹配，Enter 无提交、Esc 恢复。</summary>
    [Fact]
    public void CtrlR_EmptyHistory_EnterSubmitsNothing()
    {
        var (shell, engine) = CreateShell();
        PressCtrlR(shell);
        Type(shell, "q");
        Press(shell, ConsoleKey.Enter);
        Assert.Empty(engine.Submits);
    }
}
