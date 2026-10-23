namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell 帮助面板集成测试（S6 任务 5 — 空闲态 '?' 弹出，对齐基准 PromptInputHelpMenu）：
/// 空输入空闲态 '?' 打开（字符不进输入框）；Esc/'?' 双键关闭；有输入时 '?' 走正常字符插入；
/// 忙时不可开（空闲态门槛）；面板模态吞字符键；View.HelpOpen 投影随开关同步；
/// 渲染高度链（TailHeight 含面板行数 — 退出清理面）。
/// </summary>
public sealed class ReplShellIntegrationHelpTests
{
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(
            engine,
            new BannerItem("1.0.0", "m", "b", "/tmp"),
            new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();
        return (shell, engine);
    }

    private static void Char(ReplShell shell, char c) =>
        shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.A, shift: false, alt: false, control: false));

    private static void Esc(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));

    private static void Question(ReplShell shell) => Char(shell, '?');

    /// <summary>空闲空输入 '?' — 打开面板（字符不进输入框）+ View 投影同步。</summary>
    [Fact]
    public void Help_OpenOnQuestion_IdleEmptyInput()
    {
        var (shell, _) = CreateShell();

        Question(shell);

        Assert.True(shell.IsHelpOpen);
        Assert.True(shell.View.HelpOpen);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>Esc 关闭面板。</summary>
    [Fact]
    public void Help_Esc_Closes()
    {
        var (shell, _) = CreateShell();
        Question(shell);

        Esc(shell);

        Assert.False(shell.IsHelpOpen);
        Assert.False(shell.View.HelpOpen);
    }

    /// <summary>'?' 二次按下也关闭面板（基准 help menu 双关闭键）。</summary>
    [Fact]
    public void Help_QuestionAgain_Closes()
    {
        var (shell, _) = CreateShell();
        Question(shell);

        Question(shell);

        Assert.False(shell.IsHelpOpen);
    }

    /// <summary>面板模态 — 打开期间字符键被吞（不进输入框、面板保持）。</summary>
    [Fact]
    public void Help_Open_SwallowsCharacterKeys()
    {
        var (shell, _) = CreateShell();
        Question(shell);

        Char(shell, 'x');
        Char(shell, 'j');

        Assert.True(shell.IsHelpOpen);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>有输入时 '?' — 走正常字符插入（面板不打开）。</summary>
    [Fact]
    public void Help_WithInputText_QuestionInsertsAsCharacter()
    {
        var (shell, _) = CreateShell();
        Char(shell, 'a');

        Question(shell);

        Assert.False(shell.IsHelpOpen);
        Assert.Equal("a?", shell.Input.Text);
    }

    /// <summary>忙时 '?' 也可打开帮助（4.18 对齐基准 — PromptInput.tsx:855 任何时刻 toggle；
    /// 旧"忙时不可开"锁定推翻）。</summary>
    [Fact]
    public void Help_WhileBusy_OpensPanel()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();

        Question(shell);

        Assert.True(shell.IsHelpOpen);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>渲染高度链 — 面板打开时 TailHeight 增加面板行数（退出清理面覆盖帮助区）。</summary>
    [Fact]
    public void Help_Open_IncreasesTailHeight()
    {
        var (shell, _) = CreateShell();
        var before = shell.View.TailHeightOf(80);

        Question(shell);

        Assert.Equal(before + HelpPanel.Height, shell.View.TailHeightOf(80));
    }

    /// <summary>渲染帧 — 面板打开时屏高含面板行数 + 帮助清单标题行写入前缓冲（输入框上方定位验证）。</summary>
    [Fact]
    public void Help_RenderFrame_WritesHelpLines()
    {
        var (shell, _) = CreateShell();
        shell.RenderAndFlush();
        var closedRows = shell.BackBuffer!.Rows;

        Question(shell);
        shell.RenderAndFlush();

        var front = shell.FrontBuffer!;
        Assert.Equal(closedRows + HelpPanel.Height, front.Rows);
        var foundTitle = false;
        for (var row = 0; row < front.Rows && !foundTitle; row++)
        {
            if (RowText.Of(front, row, 2, Math.Min(20, front.Cols - 2)).Contains("快捷键"))
            {
                foundTitle = true;
            }
        }

        Assert.True(foundTitle, "前缓冲应包含帮助面板标题行");
    }
}
