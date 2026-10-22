namespace NativeTui.Tests.Repl;

/// <summary>
/// 消息操作复制测试（M8-6）— 对齐基准 messageActions.tsx 键位语义：
/// 光标导航态 c 复制选中消息全文 / p 复制工具主参数（按消息项类型取文本：工具调用取 args —
/// 渲染行无损提取、用户/助手/工具结果取 Text、diff 取 Summary[p]/Summary+补丁全量[c]、
/// Markdown 递归块拼接）、复制后不退出导航（继续移动）、剪贴板未注入静默降级（对齐
/// CopyLastAssistantMessage 先例）、复制反馈消息插到操作条之前；底部操作条 MessageActionsBar
/// 导航态挂载于 spinner 之前（闲时 spinner 高度 0 = 操作条即滚动区末行，视觉输入框上方）、
/// Esc 退出卸载、分组展开两级 Esc 期间保持、不参与导航不被选中。
/// </summary>
public sealed class MessageActionsCopyTests
{
    /// <summary>构造测试 shell（StringWriter 收渲染输出，固定尺寸源，剪贴板委托捕获到 copied；
    /// clipboard=false 模拟宿主未注入剪贴板能力）。</summary>
    private static (ReplShell Shell, List<string> Copied) CreateShell(IReadOnlyList<IScrollItem> seed, bool clipboard = true)
    {
        var copied = new List<string>();
        var engine = new FakeReplEngine();
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var shell = new ReplShell(engine, banner, new StringWriter(), seedMessages: seed,
            sizeSource: new FakeTerminalSizeSource(80, 24),
            clipboard: clipboard ? copied.Add : null);
        return (shell, copied);
    }

    /// <summary>构造三代理组（不可复制类型 no-op 场景）。</summary>
    private static SubagentGroupCoordinator CreateGroup()
    {
        var coordinator = new SubagentGroupCoordinator();
        coordinator.AddAgent("a1", "explore", "搜索");
        coordinator.AddAgent("a2", "reviewer", "审查");
        return coordinator;
    }

    /// <summary>构造带补丁的 diff 结果项（p=Summary / c=Summary+补丁全量场景）。</summary>
    private static DiffResultItem CreateDiff() => new("Edited 2 files",
    [
        new DiffHunk
        {
            Header = "@@ -1,2 +1,3 @@",
            Lines =
            [
                new DiffLine { Kind = DiffLineKind.Context, Content = "上下文" },
                new DiffLine { Kind = DiffLineKind.Removed, Content = "旧行" },
                new DiffLine { Kind = DiffLineKind.Added, Content = "新行" },
            ],
        },
    ]);

    /// <summary>构造两块 Markdown 文档项（段落 + 代码块 — 全文递归拼接场景）。</summary>
    private static MarkdownMessageItem CreateMarkdown() => new(new MarkdownDocument(
    [
        new MarkdownParagraph([new MarkdownSegment("第一段", MarkdownStyle.None)]),
        new MarkdownCode("cs", "var x = 1;"),
    ]));

    /// <summary>Shift+↑ 键（进入消息光标导航）。</summary>
    private static ConsoleKeyInfo ShiftUp() => new('\0', ConsoleKey.UpArrow, shift: true, alt: false, control: false);

    /// <summary>普通 ↑ 键。</summary>
    private static ConsoleKeyInfo Up() => new('\0', ConsoleKey.UpArrow, shift: false, alt: false, control: false);

    /// <summary>普通 ↓ 键。</summary>
    private static ConsoleKeyInfo Down() => new('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: false);

    /// <summary>Ctrl+↓ 键（末条可导航消息）。</summary>
    private static ConsoleKeyInfo CtrlDown() => new('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: true);

    /// <summary>Enter 键。</summary>
    private static ConsoleKeyInfo Enter() => new('\r', ConsoleKey.Enter, shift: false, alt: false, control: false);

    /// <summary>Esc 键。</summary>
    private static ConsoleKeyInfo Esc() => new('\0', ConsoleKey.Escape, shift: false, alt: false, control: false);

    /// <summary>可打印字符键。</summary>
    private static ConsoleKeyInfo Char(char c) => new(c, ConsoleKey.None, shift: false, alt: false, control: false);

    /// <summary>滚动区内 "已复制到剪贴板" 反馈消息索引（-1 = 无）。</summary>
    private static int IndexOfFeedback(IReadOnlyList<IScrollItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is AssistantMessageItem { Text: "已复制到剪贴板" })
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>滚动区内操作条索引（-1 = 未挂载）。</summary>
    private static int IndexOfActionBar(IReadOnlyList<IScrollItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is MessageActionsBar)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>全屏文本包含判断（逐行拼接 cell 文本，跳过宽字符占位尾格）。</summary>
    private static bool ScreenContains(Screen screen, string text)
    {
        var builder = new StringBuilder();
        for (var row = 0; row < screen.Rows; row++)
        {
            builder.Clear();
            for (var col = 0; col < screen.Cols; col++)
            {
                var cell = screen[row, col];
                if (cell.Width == CellWidth.SpacerTail)
                {
                    continue;
                }

                builder.Append(cell.Value.ToString());
            }

            if (builder.ToString().Contains(text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // ---- c / p 复制（按消息项类型取文本） ----

    /// <summary>c — 复制选中用户消息全文（Text）+ 复制后不退出导航（选中保持，继续可移动）。</summary>
    [Fact]
    public void Copy_c_CopiesSelectedUserText_AndStaysNavigating()
    {
        var (shell, copied) = CreateShell([new UserMessageItem("q1"), new AssistantMessageItem("a1"), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        Assert.Equal(3, shell.CursorNav.SelectedIndex);

        shell.HandleKey(Char('c'));

        Assert.Single(copied);
        Assert.Equal("q2", copied[0]);
        Assert.True(shell.CursorNav.IsCursorActive);
        Assert.Equal(3, shell.CursorNav.SelectedIndex);
    }

    /// <summary>c — 导航到助手消息后复制其全文（Text）。</summary>
    [Fact]
    public void Copy_c_CopiesAssistantText()
    {
        var (shell, copied) = CreateShell([new UserMessageItem("q1"), new AssistantMessageItem("a1"), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        shell.HandleKey(Up());

        shell.HandleKey(Char('c'));

        Assert.Single(copied);
        Assert.Equal("a1", copied[0]);
    }

    /// <summary>c — 工具结果消息复制其全文（Text）。</summary>
    [Fact]
    public void Copy_c_CopiesToolResultText()
    {
        var (shell, copied) = CreateShell([new UserMessageItem("q1"), new ToolResultItem("结果正文"), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        shell.HandleKey(Up());

        shell.HandleKey(Char('c'));

        Assert.Single(copied);
        Assert.Equal("结果正文", copied[0]);
    }

    /// <summary>p — 工具调用消息复制主参数（args — 渲染行无损提取，基准 PRIMARY_INPUT 本地简化）。</summary>
    [Fact]
    public void Copy_p_CopiesToolCallArgs()
    {
        var (shell, copied) = CreateShell([new UserMessageItem("q1"), new ToolCallItem("Read", "src/main.rs"), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        shell.HandleKey(Up());

        shell.HandleKey(Char('p'));

        Assert.Single(copied);
        Assert.Equal("src/main.rs", copied[0]);
    }

    /// <summary>c — 工具调用消息复制全文（工具名 + 括号参数 — 渲染行剥 ● 前缀形态）。</summary>
    [Fact]
    public void Copy_c_CopiesToolCallToolNameAndArgs()
    {
        var (shell, copied) = CreateShell([new UserMessageItem("q1"), new ToolCallItem("Read", "src/main.rs"), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        shell.HandleKey(Up());

        shell.HandleKey(Char('c'));

        Assert.Single(copied);
        Assert.Equal("Read(src/main.rs)", copied[0]);
    }

    /// <summary>p — 用户/助手/工具结果消息主参数 = 全文（Text）。</summary>
    [Fact]
    public void Copy_p_CopiesPlainMessageText()
    {
        var (shell, copied) = CreateShell([new UserMessageItem("q1"), new AssistantMessageItem("a1"), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        shell.HandleKey(Up());

        shell.HandleKey(Char('p'));

        Assert.Single(copied);
        Assert.Equal("a1", copied[0]);
    }

    /// <summary>diff — p 复制 Summary；c 复制 Summary + 补丁全量（hunk 头 + +/- 前缀行）。</summary>
    [Fact]
    public void Copy_Diff_pSummary_cFullPatch()
    {
        var (shell, copied) = CreateShell([new UserMessageItem("q1"), CreateDiff(), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        shell.HandleKey(Up());

        shell.HandleKey(Char('p'));
        Assert.Single(copied);
        Assert.Equal("Edited 2 files", copied[0]);

        shell.HandleKey(Char('c'));
        Assert.Equal(2, copied.Count);
        Assert.Equal("Edited 2 files\n@@ -1,2 +1,3 @@\n 上下文\n-旧行\n+新行", copied[1]);
    }

    /// <summary>c — Markdown 消息复制全文（块递归拼接：段落 + 代码块，块间换行）。</summary>
    [Fact]
    public void Copy_c_CopiesMarkdownBlocks()
    {
        var (shell, copied) = CreateShell([new UserMessageItem("q1"), CreateMarkdown(), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        shell.HandleKey(Up());

        shell.HandleKey(Char('c'));

        Assert.Single(copied);
        Assert.Equal("第一段\nvar x = 1;", copied[0]);
    }

    /// <summary>c/p — 选中不可复制类型（子代理分组）静默 no-op（不崩、不退出导航、无反馈消息）。</summary>
    [Fact]
    public void Copy_OnNonCopyableGroupItem_NoOpSilent()
    {
        var (shell, copied) = CreateShell([new UserMessageItem("q1"), new SubagentGroupItem(CreateGroup())]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        shell.HandleKey(Down());

        shell.HandleKey(Char('c'));
        shell.HandleKey(Char('p'));

        Assert.Empty(copied);
        Assert.True(shell.CursorNav.IsCursorActive);
        Assert.Equal(-1, IndexOfFeedback(shell.View.Messages.Items));
    }

    /// <summary>剪贴板委托未注入（null）— c/p 静默降级无操作（不崩、无反馈消息，对齐 CopyLastAssistantMessage 先例）。</summary>
    [Fact]
    public void Copy_WithoutClipboardDelegate_SilentNoOp()
    {
        var (shell, copied) = CreateShell([new UserMessageItem("q1")], clipboard: false);
        shell.SeedContent();
        var countBeforeActionsBar = shell.View.Messages.Count;

        shell.HandleKey(ShiftUp());
        shell.HandleKey(Char('c'));
        shell.HandleKey(Char('p'));

        Assert.Empty(copied);
        Assert.True(shell.CursorNav.IsCursorActive);
        Assert.Equal(countBeforeActionsBar + 1, shell.View.Messages.Count);
        Assert.Equal(-1, IndexOfFeedback(shell.View.Messages.Items));
    }

    /// <summary>复制反馈 — "已复制到剪贴板" 消息插到操作条之前（消息流末尾语义，操作条/spinner 尾部常驻保持）
    /// + 反馈后继续导航与复制（选中索引不受反馈插入影响）。</summary>
    [Fact]
    public void Copy_AppendsFeedbackAboveActionBar_NavigationStillMoves()
    {
        var (shell, copied) = CreateShell([new UserMessageItem("q1"), new AssistantMessageItem("a1"), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        shell.HandleKey(Char('c'));

        var items = shell.View.Messages.Items;
        var feedbackIndex = IndexOfFeedback(items);
        var barIndex = IndexOfActionBar(items);
        Assert.True(feedbackIndex >= 0, "反馈消息应已追加");
        Assert.True(barIndex > feedbackIndex, "反馈消息应在操作条之前");
        Assert.IsType<SpinnerItem>(items[^1]);

        shell.HandleKey(Up());
        Assert.Equal(2, shell.CursorNav.SelectedIndex);
        shell.HandleKey(Char('p'));
        Assert.Equal(2, copied.Count);
        Assert.Equal("a1", copied[1]);
    }

    // ---- 底部操作条挂载 / 卸载 / 渲染 / 导航语义 ----

    /// <summary>操作条挂载 — 导航态激活挂到 spinner 之前（spinner 恒最后，操作条为倒数第二项）；
    /// Esc 退出后卸载（列表复原不含操作条项）。</summary>
    [Fact]
    public void ActionBar_MountsBeforeSpinnerWhileNavigating_UnmountsOnExit()
    {
        var (shell, _) = CreateShell([new UserMessageItem("q1")]);
        shell.SeedContent();

        shell.HandleKey(ShiftUp());
        var items = shell.View.Messages.Items;
        Assert.True(IndexOfActionBar(items) >= 0);
        Assert.IsType<MessageActionsBar>(items[^2]);
        Assert.IsType<SpinnerItem>(items[^1]);

        shell.HandleKey(Esc());
        Assert.Equal(-1, IndexOfActionBar(shell.View.Messages.Items));
    }

    /// <summary>操作条渲染 — 导航态激活期间帧内含键位提示（enter 展开 …）；Esc 退出后不再渲染。</summary>
    [Fact]
    public void ActionBar_RendersHintsWhileNavigating_DisappearsAfterExit()
    {
        var (shell, _) = CreateShell([new UserMessageItem("q1"), new AssistantMessageItem("a1")]);
        shell.SeedContent();

        shell.HandleKey(ShiftUp());
        shell.RenderAndFlush();
        shell.RenderAndFlush();
        Assert.True(ScreenContains(shell.BackBuffer!, "enter 展开"));
        Assert.True(ScreenContains(shell.BackBuffer!, "c 复制"));
        Assert.True(ScreenContains(shell.BackBuffer!, "p 复制参数"));
        Assert.True(ScreenContains(shell.BackBuffer!, "esc 返回"));

        shell.HandleKey(Esc());
        shell.RenderAndFlush();
        shell.RenderAndFlush();
        Assert.False(ScreenContains(shell.BackBuffer!, "enter 展开"));
    }

    /// <summary>操作条不参与导航 — Ctrl+↓ 跳末条可导航消息不落到操作条（非 ICursorNavigable）。</summary>
    [Fact]
    public void ActionBar_DoesNotParticipateInNavigation()
    {
        var (shell, _) = CreateShell([new UserMessageItem("q1"), new AssistantMessageItem("a1"), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        shell.HandleKey(Up());
        Assert.Equal(2, shell.CursorNav.SelectedIndex);

        shell.HandleKey(CtrlDown());
        Assert.Equal(3, shell.CursorNav.SelectedIndex);
        Assert.IsNotType<MessageActionsBar>(shell.View.Messages.Items[shell.CursorNav.SelectedIndex]);
    }

    /// <summary>两级 Esc — 有展开组时第一次 Esc 仅收组（导航保持、操作条保持）；第二次 Esc 退出并卸载操作条。</summary>
    [Fact]
    public void ActionBar_KeptWhileCollapsingGroup_RemovedOnSecondEscape()
    {
        var group = CreateGroup();
        var (shell, _) = CreateShell([new UserMessageItem("q1"), new SubagentGroupItem(group)]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        shell.HandleKey(Down());
        shell.HandleKey(Enter());
        Assert.True(group.IsExpanded);

        shell.HandleKey(Esc());
        Assert.False(group.IsExpanded);
        Assert.True(shell.CursorNav.IsCursorActive);
        Assert.True(IndexOfActionBar(shell.View.Messages.Items) >= 0);

        shell.HandleKey(Esc());
        Assert.False(shell.CursorNav.IsCursorActive);
        Assert.Equal(-1, IndexOfActionBar(shell.View.Messages.Items));
    }

    /// <summary>操作条键位提示文案 — 间隔符 · 与全链键位（对齐基准 messageActions 底部提示条形态）。</summary>
    [Fact]
    public void ActionBar_HintsText_UsesMiddotSeparatedFullKeySet()
    {
        Assert.Equal("enter 展开 · c 复制 · p 复制参数 · ↑↓ 导航 · esc 返回", MessageActionsBar.HintsText);
    }
}
