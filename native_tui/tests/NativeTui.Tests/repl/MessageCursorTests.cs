namespace NativeTui.Tests.Repl;

/// <summary>
/// 消息光标导航测试（交互层 Step1）— 对齐基准消息导航系统（MESSAGE_ACTIONS：Shift+↑ 进入 enterCursor
/// 定位最后一条用户消息、↑/↓/k/j 逐条、Shift+↑/↓ 跳用户消息、Ctrl+↑/↓ 首末、Enter 展开 grouped_tool_use、
/// Esc 两级退出先收起展开再退出）：
/// MessageCursor 协调器纯状态机（Begin 定位/相邻可导航移动/用户消息跳转/首末/退出）+
/// SubagentGroupCoordinator 展开态（全量窗口替代 min(10,n) 折叠）+ ReplShell 第四交互态键路由
/// （Shift+↑ 进入、光标态模态吞键）+ ScrollBox 选中高亮（整块背景覆写 MessageActionsBackground）。
/// </summary>
public sealed class MessageCursorTests
{
    /// <summary>构造测试 shell（StringWriter 收渲染输出，固定尺寸源，seed 消息可缺省）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell(IReadOnlyList<IScrollItem>? seed = null)
    {
        var engine = new FakeReplEngine();
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var shell = new ReplShell(engine, banner, new StringWriter(), seedMessages: seed, sizeSource: new FakeTerminalSizeSource(80, 24));
        return (shell, engine);
    }

    /// <summary>构造混合消息项序列（banner[0] / q1[1] / a1[2] / tool[3] / q2[4] / group[5] / spinner[6] — 可导航 = 1..5）。</summary>
    private static IScrollItem[] CreateItems() =>
    [
        new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp"),
        new UserMessageItem("q1"),
        new AssistantMessageItem("a1"),
        new ToolCallItem("Read", "f.cs"),
        new UserMessageItem("q2"),
        new SubagentGroupItem(CreateGroup()),
        new SpinnerItem { Message = string.Empty },
    ];

    /// <summary>构造三代理组（供可导航混合序列的分组项）。</summary>
    private static SubagentGroupCoordinator CreateGroup()
    {
        var coordinator = new SubagentGroupCoordinator();
        coordinator.AddAgent("a1", "explore", "搜索");
        coordinator.AddAgent("a2", "reviewer", "审查");
        coordinator.AddAgent("a3", "builder", "编译");
        return coordinator;
    }

    /// <summary>Shift+↑ 键（进入消息光标导航）。</summary>
    private static ConsoleKeyInfo ShiftUp() => new('\0', ConsoleKey.UpArrow, shift: true, alt: false, control: false);

    /// <summary>Shift+↓ 键（跳用户消息向下）。</summary>
    private static ConsoleKeyInfo ShiftDown() => new('\0', ConsoleKey.DownArrow, shift: true, alt: false, control: false);

    /// <summary>普通 ↑ 键。</summary>
    private static ConsoleKeyInfo Up() => new('\0', ConsoleKey.UpArrow, shift: false, alt: false, control: false);

    /// <summary>普通 ↓ 键。</summary>
    private static ConsoleKeyInfo Down() => new('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: false);

    /// <summary>Ctrl+↑ 键（首条可导航消息）。</summary>
    private static ConsoleKeyInfo CtrlUp() => new('\0', ConsoleKey.UpArrow, shift: false, alt: false, control: true);

    /// <summary>Ctrl+↓ 键（末条可导航消息）。</summary>
    private static ConsoleKeyInfo CtrlDown() => new('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: true);

    /// <summary>Enter 键。</summary>
    private static ConsoleKeyInfo Enter() => new('\r', ConsoleKey.Enter, shift: false, alt: false, control: false);

    /// <summary>Esc 键。</summary>
    private static ConsoleKeyInfo Esc() => new('\0', ConsoleKey.Escape, shift: false, alt: false, control: false);

    /// <summary>可打印字符键。</summary>
    private static ConsoleKeyInfo Char(char c) => new(c, ConsoleKey.None, shift: false, alt: false, control: false);

    // ---- MessageCursor 协调器（纯状态机） ----

    /// <summary>Begin — 定位最后一条用户消息（基准 enterCursor scan 语义）。</summary>
    [Fact]
    public void Begin_LocatesLastUserMessage()
    {
        var cursor = new MessageCursor();
        cursor.Begin(CreateItems());

        Assert.True(cursor.IsCursorActive);
        Assert.Equal(4, cursor.SelectedIndex);
    }

    /// <summary>Begin — 无用户消息时回退定位最后一条可导航消息。</summary>
    [Fact]
    public void Begin_NoUserMessage_FallsBackToLastNavigable()
    {
        var cursor = new MessageCursor();
        var items = new IScrollItem[]
        {
            new BannerItem("1.0.0", "m", "API", "/tmp"),
            new AssistantMessageItem("a1"),
            new ToolResultItem("结果"),
            new SpinnerItem { Message = string.Empty },
        };
        cursor.Begin(items);

        Assert.True(cursor.IsCursorActive);
        Assert.Equal(2, cursor.SelectedIndex);
    }

    /// <summary>Begin — 无可导航消息（仅 banner/spinner）不激活。</summary>
    [Fact]
    public void Begin_NoNavigableMessages_StaysInactive()
    {
        var cursor = new MessageCursor();
        var items = new IScrollItem[]
        {
            new BannerItem("1.0.0", "m", "API", "/tmp"),
            new SpinnerItem { Message = string.Empty },
        };
        cursor.Begin(items);

        Assert.False(cursor.IsCursorActive);
        Assert.Equal(-1, cursor.SelectedIndex);
    }

    /// <summary>Move — 相邻可导航消息步进（跳过 banner/spinner 非导航项）+ 边界 clamp 不环绕。</summary>
    [Fact]
    public void Move_StepsBetweenNavigableItems_ClampsAtBounds()
    {
        var items = CreateItems();
        var cursor = new MessageCursor();
        cursor.Begin(items);
        Assert.Equal(4, cursor.SelectedIndex);

        cursor.Move(items, -1);
        Assert.Equal(3, cursor.SelectedIndex);

        cursor.Move(items, -1);
        Assert.Equal(2, cursor.SelectedIndex);

        cursor.Move(items, 1);
        Assert.Equal(3, cursor.SelectedIndex);

        cursor.Move(items, 1);
        cursor.Move(items, 1);
        Assert.Equal(5, cursor.SelectedIndex);

        cursor.Move(items, 1);
        Assert.Equal(5, cursor.SelectedIndex);

        var top = new MessageCursor();
        top.Begin(items);
        top.Move(items, 1);
        top.Move(items, -1);
        top.Move(items, -1);
        Assert.Equal(3, top.SelectedIndex);

        top.Move(items, -1);
        top.Move(items, -1);
        top.Move(items, -1);
        Assert.Equal(1, top.SelectedIndex);
    }

    /// <summary>MoveToUserMessage — 跳到相邻用户消息（向前/向后）；无后续用户消息保持不动。</summary>
    [Fact]
    public void MoveToUserMessage_JumpsBetweenUserMessages()
    {
        var items = CreateItems();
        var cursor = new MessageCursor();
        cursor.Begin(items);
        Assert.Equal(4, cursor.SelectedIndex);

        cursor.MoveToUserMessage(items, -1);
        Assert.Equal(1, cursor.SelectedIndex);

        cursor.MoveToUserMessage(items, -1);
        Assert.Equal(1, cursor.SelectedIndex);

        cursor.MoveToUserMessage(items, 1);
        Assert.Equal(4, cursor.SelectedIndex);

        cursor.MoveToUserMessage(items, 1);
        Assert.Equal(4, cursor.SelectedIndex);
    }

    /// <summary>MoveToUserMessage — 起点不在用户消息上时，向指定方向找最近用户消息。</summary>
    [Fact]
    public void MoveToUserMessage_FromNonUserMessage_FindsNearestInDirection()
    {
        var items = CreateItems();
        var cursor = new MessageCursor();
        cursor.Begin(items);
        cursor.Move(items, -1);
        cursor.Move(items, -1);
        Assert.Equal(2, cursor.SelectedIndex);

        cursor.MoveToUserMessage(items, 1);
        Assert.Equal(4, cursor.SelectedIndex);
    }

    /// <summary>MoveToEdge — 首/末可导航消息（Ctrl+↑/↓）。</summary>
    [Fact]
    public void MoveToEdge_FirstAndLastNavigable()
    {
        var items = CreateItems();
        var cursor = new MessageCursor();
        cursor.Begin(items);

        cursor.MoveToEdge(items, first: true);
        Assert.Equal(1, cursor.SelectedIndex);

        cursor.MoveToEdge(items, first: false);
        Assert.Equal(5, cursor.SelectedIndex);
    }

    /// <summary>Exit — 清空选中（SelectedIndex 归 -1，幂等）。</summary>
    [Fact]
    public void Exit_ClearsState()
    {
        var cursor = new MessageCursor();
        cursor.Begin(CreateItems());
        cursor.Exit();

        Assert.False(cursor.IsCursorActive);
        Assert.Equal(-1, cursor.SelectedIndex);

        cursor.Exit();
        Assert.False(cursor.IsCursorActive);
    }

    // ---- SubagentGroupCoordinator 展开态 ----

    /// <summary>展开 — VisibleCount 全量、HiddenCount 归零（突破 min(10,n) 窗口）。</summary>
    [Fact]
    public void IsExpanded_FullWindowWithoutHidden()
    {
        var coordinator = new SubagentGroupCoordinator();
        for (var i = 0; i < 13; i++)
        {
            coordinator.AddAgent($"a{i}", "explore", $"任务{i}");
        }

        Assert.False(coordinator.IsExpanded);
        Assert.Equal(10, coordinator.VisibleCount);
        Assert.Equal(3, coordinator.HiddenCount);

        coordinator.IsExpanded = true;
        Assert.Equal(13, coordinator.VisibleCount);
        Assert.Equal(0, coordinator.HiddenCount);

        coordinator.IsExpanded = false;
        Assert.Equal(10, coordinator.VisibleCount);
        Assert.Equal(3, coordinator.HiddenCount);
    }

    /// <summary>展开态渲染高度 — margin 1 + 组头 1 + 全量 N×2（无溢出行）。</summary>
    [Fact]
    public void MeasureHeight_ExpandedRendersAllAgents()
    {
        var coordinator = new SubagentGroupCoordinator();
        for (var i = 0; i < 13; i++)
        {
            coordinator.AddAgent($"a{i}", "explore", $"任务{i}");
        }

        var item = new SubagentGroupItem(coordinator);
        Assert.Equal(23, item.MeasureHeight(60));

        coordinator.IsExpanded = true;
        Assert.Equal(28, item.MeasureHeight(60));
    }

    /// <summary>展开态渲染 — 第 13 个代理（折叠时溢出不可见）完整可见 + 无 "and N more…" 溢出行
    /// （混合类型规避全同型 hideType 形态，锁定 "└─ type (desc)" 完整行）。</summary>
    [Fact]
    public void Render_ExpandedShowsAllAgentsWithoutOverflow()
    {
        var coordinator = new SubagentGroupCoordinator();
        for (var i = 0; i < 13; i++)
        {
            coordinator.AddAgent($"a{i}", i % 2 == 0 ? "explore" : "reviewer", $"任务{i}");
        }

        coordinator.IsExpanded = true;
        using var screen = new Screen(cols: 60, rows: 32);
        new SubagentGroupItem(coordinator).Render(screen, new Rect(0, 0, 60, 32), Theme.Dark);

        Assert.StartsWith("├─ reviewer (任务11)", RowText.Of(screen, 24, 0, 60));
        Assert.StartsWith("└─ explore (任务12)", RowText.Of(screen, 26, 0, 60));
        Assert.DoesNotContain("and 3 more", RowText.Of(screen, 27, 0, 60));
    }

    // ---- ReplShell 第四交互态键路由 ----

    /// <summary>Shift+↑ 进入光标导航 — 定位最后一条用户消息（items: banner/q1/a1/q2/spinner → q2 索引 3）。</summary>
    [Fact]
    public void ShiftUpArrow_EntersCursorNav_AtLastUserMessage()
    {
        var (shell, _) = CreateShell(seed: [new UserMessageItem("q1"), new AssistantMessageItem("a1"), new UserMessageItem("q2")]);
        shell.SeedContent();

        shell.HandleKey(ShiftUp());

        Assert.True(shell.CursorNav.IsCursorActive);
        Assert.Equal(3, shell.CursorNav.SelectedIndex);
    }

    /// <summary>光标态 ↑/↓ — 相邻可导航消息移动（跳过 spinner 尾项）+ 首末 clamp 不环绕。</summary>
    [Fact]
    public void CursorNav_PlainArrowsMoveSelection()
    {
        var (shell, _) = CreateShell(seed: [new UserMessageItem("q1"), new AssistantMessageItem("a1"), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        Assert.Equal(3, shell.CursorNav.SelectedIndex);

        shell.HandleKey(Down());
        Assert.Equal(3, shell.CursorNav.SelectedIndex);

        shell.HandleKey(Up());
        Assert.Equal(2, shell.CursorNav.SelectedIndex);

        shell.HandleKey(Down());
        Assert.Equal(3, shell.CursorNav.SelectedIndex);

        shell.HandleKey(Up());
        shell.HandleKey(Up());
        Assert.Equal(1, shell.CursorNav.SelectedIndex);

        shell.HandleKey(Up());
        Assert.Equal(1, shell.CursorNav.SelectedIndex);
    }

    /// <summary>光标态 k/j — vim 键逐条移动（基准 MessageActions 上下文绑定）。</summary>
    [Fact]
    public void CursorNav_KAndJMoveSelection()
    {
        var (shell, _) = CreateShell(seed: [new UserMessageItem("q1"), new AssistantMessageItem("a1"), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        Assert.Equal(3, shell.CursorNav.SelectedIndex);

        shell.HandleKey(Char('k'));
        Assert.Equal(2, shell.CursorNav.SelectedIndex);

        shell.HandleKey(Char('j'));
        Assert.Equal(3, shell.CursorNav.SelectedIndex);
    }

    /// <summary>光标态 Shift+↑/↓ — 跳用户消息。</summary>
    [Fact]
    public void CursorNav_ShiftArrowsJumpUserMessages()
    {
        var (shell, _) = CreateShell(seed: [new UserMessageItem("q1"), new AssistantMessageItem("a1"), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        Assert.Equal(3, shell.CursorNav.SelectedIndex);

        shell.HandleKey(ShiftUp());
        Assert.Equal(1, shell.CursorNav.SelectedIndex);

        shell.HandleKey(ShiftDown());
        Assert.Equal(3, shell.CursorNav.SelectedIndex);
    }

    /// <summary>光标态 Ctrl+↑/↓ — 首末可导航消息。</summary>
    [Fact]
    public void CursorNav_CtrlArrowsJumpToEdges()
    {
        var (shell, _) = CreateShell(seed: [new UserMessageItem("q1"), new AssistantMessageItem("a1"), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());

        shell.HandleKey(CtrlUp());
        Assert.Equal(1, shell.CursorNav.SelectedIndex);

        shell.HandleKey(CtrlDown());
        Assert.Equal(3, shell.CursorNav.SelectedIndex);
    }

    /// <summary>光标态 Enter — 选中分组项时切换展开/收起（对齐基准 Enter 展开 grouped_tool_use）。</summary>
    [Fact]
    public void CursorNav_EnterTogglesGroupExpansion()
    {
        var group = CreateGroup();
        var (shell, _) = CreateShell(seed: [new UserMessageItem("q1"), new SubagentGroupItem(group), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        Assert.Equal(3, shell.CursorNav.SelectedIndex);

        shell.HandleKey(Up());
        Assert.Equal(2, shell.CursorNav.SelectedIndex);
        Assert.False(group.IsExpanded);

        shell.HandleKey(Enter());
        Assert.True(group.IsExpanded);

        shell.HandleKey(Enter());
        Assert.False(group.IsExpanded);
    }

    /// <summary>光标态 Enter — 选中非分组消息 no-op（不退出导航）。</summary>
    [Fact]
    public void CursorNav_EnterOnPlainMessage_NoOp()
    {
        var (shell, _) = CreateShell(seed: [new UserMessageItem("q1")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        Assert.Equal(1, shell.CursorNav.SelectedIndex);

        shell.HandleKey(Enter());

        Assert.True(shell.CursorNav.IsCursorActive);
        Assert.Equal(1, shell.CursorNav.SelectedIndex);
    }

    /// <summary>光标态 Esc 两级退出 — 有展开组先收起（导航保持）；再 Esc 才退出。</summary>
    [Fact]
    public void CursorNav_EscapeCollapsesExpandedGroup_ThenSecondEscapeExits()
    {
        var group = CreateGroup();
        var (shell, _) = CreateShell(seed: [new SubagentGroupItem(group), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        shell.HandleKey(Up());
        shell.HandleKey(Enter());
        Assert.True(group.IsExpanded);

        shell.HandleKey(Esc());
        Assert.False(group.IsExpanded);
        Assert.True(shell.CursorNav.IsCursorActive);

        shell.HandleKey(Esc());
        Assert.False(shell.CursorNav.IsCursorActive);
    }

    /// <summary>光标态 Esc — 无展开组直接退出导航。</summary>
    [Fact]
    public void CursorNav_EscapeWithoutExpansion_ExitsImmediately()
    {
        var (shell, _) = CreateShell(seed: [new UserMessageItem("q1")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        Assert.True(shell.CursorNav.IsCursorActive);

        shell.HandleKey(Esc());
        Assert.False(shell.CursorNav.IsCursorActive);
    }

    /// <summary>光标态模态 — 字符键不进输入缓冲（基准全屏模式 PromptInput 卸载语义）。</summary>
    [Fact]
    public void CursorNav_SwallowsCharacterKeys_InputUntouched()
    {
        var (shell, _) = CreateShell(seed: [new UserMessageItem("q1")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());

        shell.HandleKey(Char('a'));
        shell.HandleKey(Char('b'));

        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>未进入光标态 — 普通 ↑ 走历史导航（不触发导航），Shift+↑ 不吞字符。</summary>
    [Fact]
    public void PlainUpArrow_WithoutCursorNav_StillNavigatesHistory()
    {
        var (shell, _) = CreateShell(seed: [new UserMessageItem("q1")]);
        shell.SeedContent();
        shell.Input.Replace("draft", cursorToStart: false);

        shell.HandleKey(Up());

        Assert.False(shell.CursorNav.IsCursorActive);
    }

    // ---- 选中高亮渲染 ----

    /// <summary>渲染高亮 — 选中项整块背景覆写 MessageActionsBackground（前景保留：正文样式前景 Text）。
    /// 双缓冲交换语义：渲染两次后 BackBuffer 携带上帧内容（同内容）。</summary>
    [Fact]
    public void RenderAndFlush_HighlightsSelectedItemBackground()
    {
        var (shell, _) = CreateShell(seed: [new UserMessageItem("q1"), new AssistantMessageItem("a1"), new UserMessageItem("q2")]);
        shell.SeedContent();
        shell.HandleKey(ShiftUp());
        shell.RenderAndFlush();
        shell.RenderAndFlush();

        var back = shell.BackBuffer!;
        Assert.True(TryFindText(back, "q2", out var q2Row, out var q2Col));
        var highlighted = back.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text, Background = Theme.Dark.MessageActionsBackground });
        Assert.Equal(highlighted, back[q2Row, q2Col].StyleId);

        Assert.True(TryFindText(back, "q1", out var q1Row, out var q1Col));
        var normal = back.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text, Background = Theme.Dark.UserMessageBackground });
        Assert.Equal(normal, back[q1Row, q1Col].StyleId);
    }

    /// <summary>无光标态 — 不高亮（渲染不变）。双缓冲交换语义：渲染两次后 BackBuffer 携带上帧内容。</summary>
    [Fact]
    public void RenderAndFlush_WithoutCursorNav_NoHighlight()
    {
        var (shell, _) = CreateShell(seed: [new UserMessageItem("q1")]);
        shell.SeedContent();
        shell.RenderAndFlush();
        shell.RenderAndFlush();

        var back = shell.BackBuffer!;
        Assert.True(TryFindText(back, "q1", out var row, out var col));
        var normal = back.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text, Background = Theme.Dark.UserMessageBackground });
        Assert.Equal(normal, back[row, col].StyleId);
    }

    /// <summary>定位两字符文本的 cell 坐标（正文 'q' 右邻 '2' 形态）。</summary>
    private static bool TryFindText(Screen screen, string text, out int row, out int col)
    {
        for (var r = 0; r < screen.Rows; r++)
        {
            for (var c = 0; c < screen.Cols - 1; c++)
            {
                if (screen[r, c].Value == new Rune(text[0]) && screen[r, c + 1].Value == new Rune(text[1]))
                {
                    row = r;
                    col = c;
                    return true;
                }
            }
        }

        row = -1;
        col = -1;
        return false;
    }
}
