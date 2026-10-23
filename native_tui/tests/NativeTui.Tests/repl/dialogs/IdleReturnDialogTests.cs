namespace NativeTui.Tests.Repl;

/// <summary>
/// IdleReturnDialog 空闲返回对话框测试（TASK066 板块 D D1 组件 + D7/D8 接线）：
/// 组件层 — 时长格式化四分支（&lt;1m/Nm/Nh/Nh Nm — 基准 formatIdleDuration 逐分支）/ 标题组合 /
/// ↑↓ clamp 导航 / enter 三选项确认 / esc dismiss / 模态吞键 / 已关闭拒键 / 渲染行序 golden +
/// 焦点行 ❯ suggestion 高亮 + 导航后高亮跟随 + MeasureHeight 一致性；
/// 集成层（Harness）— 阈值内直发不弹 / 超阈值拦截挂载+暂存 / 首回合不触发 / token 门槛不足不弹 /
/// 斜杠命令豁免 / esc 恢复输入丢弃提交 / continue 原样重发 / clear 降级重发 / never 后不再弹。
/// </summary>
public sealed class IdleReturnDialogTests
{
    // ---------------- 组件层 ----------------

    /// <summary>&lt;1 分钟 — "&amp;lt; 1m"（基准 IdleReturnDialog.tsx:105-107）。</summary>
    [Fact]
    public void FormatIdleDuration_BelowOneMinute_LessThanOneMinute()
    {
        Assert.Equal("< 1m", IdleReturnDialog.FormatIdleDuration(0));
        Assert.Equal("< 1m", IdleReturnDialog.FormatIdleDuration(0.5));
        Assert.Equal("< 1m", IdleReturnDialog.FormatIdleDuration(0.99));
    }

    /// <summary>&lt;60 分钟 — 向下取整分钟 "Nm"（基准 :108-110 Math.floor）。</summary>
    [Fact]
    public void FormatIdleDuration_UnderHour_FloorsMinutes()
    {
        Assert.Equal("1m", IdleReturnDialog.FormatIdleDuration(1));
        Assert.Equal("5m", IdleReturnDialog.FormatIdleDuration(5.7));
        Assert.Equal("59m", IdleReturnDialog.FormatIdleDuration(59.9));
    }

    /// <summary>整小时 — 仅 "Nh"（基准 :113-115 余分钟为 0 分支）。</summary>
    [Fact]
    public void FormatIdleDuration_ExactHour_HoursOnly()
    {
        Assert.Equal("1h", IdleReturnDialog.FormatIdleDuration(60));
        Assert.Equal("2h", IdleReturnDialog.FormatIdleDuration(120));
    }

    /// <summary>小时 + 分钟 — "Nh Nm"（基准 :116-117 复合分支）。</summary>
    [Fact]
    public void FormatIdleDuration_HourAndMinutes_Compound()
    {
        Assert.Equal("1h 15m", IdleReturnDialog.FormatIdleDuration(75));
        Assert.Equal("1h 30m", IdleReturnDialog.FormatIdleDuration(90));
        Assert.Equal("2h 5m", IdleReturnDialog.FormatIdleDuration(125.9));
    }

    /// <summary>构造态 — 焦点在"继续此对话"（选项 0），未决未关闭（对齐基准 Select 默认焦点首项）。</summary>
    [Fact]
    public void Construct_InitialFocusOnContinue()
    {
        var dialog = new IdleReturnDialog(75, 100_000);

        Assert.Equal(0, dialog.SelectedIndex);
        Assert.Equal(IdleReturnAction.Continue, dialog.FocusedAction);
        Assert.Null(dialog.SelectedAction);
        Assert.False(dialog.IsClosed);
    }

    /// <summary>↑↓ 导航 — clamp 不环绕（顶部/底部边界钳制）。</summary>
    [Fact]
    public void ArrowKeys_MoveSelection_ClampedAtBounds()
    {
        var dialog = new IdleReturnDialog(75, 100_000);

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.UpArrow)));
        Assert.Equal(0, dialog.SelectedIndex);

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.Equal(2, dialog.SelectedIndex);

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.Equal(2, dialog.SelectedIndex);
    }

    /// <summary>enter 默认焦点 — 确认 Continue（继续此对话）。</summary>
    [Fact]
    public void Enter_DefaultSelection_ReturnsContinue()
    {
        var dialog = new IdleReturnDialog(75, 100_000);

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Return)));

        Assert.True(dialog.IsClosed);
        Assert.Equal(IdleReturnAction.Continue, dialog.SelectedAction);
    }

    /// <summary>enter 第二选项 — 确认 Clear（作为新会话发送）。</summary>
    [Fact]
    public void Enter_SecondOption_ReturnsClear()
    {
        var dialog = new IdleReturnDialog(75, 100_000);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));
        dialog.TryHandleKey(KeyPress.Named(KeyKind.Return));

        Assert.True(dialog.IsClosed);
        Assert.Equal(IdleReturnAction.Clear, dialog.SelectedAction);
    }

    /// <summary>enter 第三选项 — 确认 Never（不再询问）。</summary>
    [Fact]
    public void Enter_ThirdOption_ReturnsNever()
    {
        var dialog = new IdleReturnDialog(75, 100_000);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));
        dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));
        dialog.TryHandleKey(KeyPress.Named(KeyKind.Return));

        Assert.True(dialog.IsClosed);
        Assert.Equal(IdleReturnAction.Never, dialog.SelectedAction);
    }

    /// <summary>esc — 取消语义（Dismiss — 基准 onDone("dismiss") → setInputValue 恢复输入框）。</summary>
    [Fact]
    public void Escape_ReturnsDismissAndCloses()
    {
        var dialog = new IdleReturnDialog(75, 100_000);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Escape)));

        Assert.True(dialog.IsClosed);
        Assert.Equal(IdleReturnAction.Dismiss, dialog.SelectedAction);
    }

    /// <summary>未接线键 — 模态吞掉（字符/Tab/ctrl 组合），状态不变。</summary>
    [Fact]
    public void UnhandledKeys_AreSwallowedModally()
    {
        var dialog = new IdleReturnDialog(75, 100_000);

        Assert.True(dialog.TryHandleKey(KeyPress.Char("x")));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Tab)));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Backspace)));
        Assert.True(dialog.TryHandleKey(new KeyPress(KeyKind.Char, "p", Ctrl: true)));

        Assert.Equal(0, dialog.SelectedIndex);
        Assert.False(dialog.IsClosed);
        Assert.Null(dialog.SelectedAction);
    }

    /// <summary>已关闭 — 拒绝后续键（TryHandleKey false 防御契约，状态不再变化）。</summary>
    [Fact]
    public void ClosedDialog_RejectsFurtherKeys()
    {
        var dialog = new IdleReturnDialog(75, 100_000);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.Escape));

        Assert.False(dialog.TryHandleKey(KeyPress.Named(KeyKind.Return)));
        Assert.Equal(IdleReturnAction.Dismiss, dialog.SelectedAction);
        Assert.True(dialog.IsClosed);
    }

    /// <summary>标题组合 — 时长 + tokens 快照拼入标题
    ///（基准 :38 `You've been away {idle} and this conversation is {tokens} tokens.` 汉化）。</summary>
    [Fact]
    public void FormattedTitle_ComposesIdleDurationAndTokens()
    {
        var dialog = new IdleReturnDialog(75, 100_000);

        Assert.Equal("你已离开 1h 15m，此对话已 100k tokens。", dialog.FormattedTitle);
    }

    /// <summary>tokens 千位缩写 — 1321 → "1.3k"（复用 MessageRenderer.FormatTokens 基准形态）。</summary>
    [Fact]
    public void FormattedTitle_Thousands_ScalesWithK()
    {
        var dialog = new IdleReturnDialog(5, 1321);

        Assert.Equal("你已离开 5m，此对话已 1.3k tokens。", dialog.FormattedTitle);
    }

    /// <summary>渲染行序 golden — margin/边框/标题/提示行/空行/三选项/空行/footer
    ///（对齐 QuickOpenDialog 形态学骨架；60 宽 12 行）。</summary>
    [Fact]
    public void Render_RowLayout_BorderTitleHintOptionsFooter()
    {
        using var screen = new Screen(cols: 60, rows: 12);
        var dialog = new IdleReturnDialog(75, 100_000);

        Assert.Equal(10, dialog.MeasureHeight(60));
        dialog.Render(screen, new Rect(0, 0, 60, 12), Theme.Dark);

        // 行序：margin0 / 边框1 / 标题2 / 提示3 / 空4 / 选项5-7 / 空8 / footer9
        Assert.Equal(new string('─', 60), RowText.Of(screen, 1, 0, 60));
        Assert.Equal(" 你已离开 1h 15m，此对话已 100k tokens。", RowText.Of(screen, 2, 0, 60).TrimEnd());
        Assert.Equal(" 如果是新任务，清空上下文将节省用量并更快。", RowText.Of(screen, 3, 0, 60).TrimEnd());
        Assert.Equal("❯ 继续此对话", RowText.Of(screen, 5, 0, 60).TrimEnd());
        Assert.Equal("  作为新会话发送", RowText.Of(screen, 6, 0, 60).TrimEnd());
        Assert.Equal("  不再询问", RowText.Of(screen, 7, 0, 60).TrimEnd());
        Assert.Equal(" ↑↓ 导航 · enter 确认 · esc 返回", RowText.Of(screen, 9, 0, 60).TrimEnd());
    }

    /// <summary>焦点行高亮 — ❯ 与选项文案 suggestion 色；非焦点行默认样式
    ///（对齐 QuickOpenDialog.RenderResultRow 分色）。</summary>
    [Fact]
    public void Render_FocusedRow_SuggestionColored()
    {
        using var screen = new Screen(cols: 60, rows: 12);
        var dialog = new IdleReturnDialog(75, 100_000);

        dialog.Render(screen, new Rect(0, 0, 60, 12), Theme.Dark);

        var suggestion = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Suggestion });
        Assert.Equal(suggestion, screen[5, 0].StyleId);
        Assert.Equal(suggestion, screen[5, 2].StyleId);
        Assert.Equal(0, screen[6, 0].StyleId);
        Assert.Equal(0, screen[6, 2].StyleId);
    }

    /// <summary>导航后渲染 — 高亮跟随焦点行（❯ 下移到第二选项）。</summary>
    [Fact]
    public void Render_AfterNavigation_FocusMovesToSecondOption()
    {
        using var screen = new Screen(cols: 60, rows: 12);
        var dialog = new IdleReturnDialog(75, 100_000);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));
        dialog.Render(screen, new Rect(0, 0, 60, 12), Theme.Dark);

        Assert.Equal("  继续此对话", RowText.Of(screen, 5, 0, 60).TrimEnd());
        Assert.Equal("❯ 作为新会话发送", RowText.Of(screen, 6, 0, 60).TrimEnd());
    }

    /// <summary>关闭后 — 零高度零渲染（宿主消费 IsClosed 后移除对话框项契约）。</summary>
    [Fact]
    public void Render_Closed_ZeroHeightAndNoPaint()
    {
        using var screen = new Screen(cols: 60, rows: 12);
        var dialog = new IdleReturnDialog(75, 100_000);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.Escape));

        Assert.Equal(0, dialog.MeasureHeight(60));
        dialog.Render(screen, new Rect(0, 0, 60, 12), Theme.Dark);
        Assert.Equal(string.Empty, RowText.Of(screen, 1, 0, 60).TrimEnd());
    }

    // ---------------- 集成层（Harness — FakeReplEngine 事件注入 + IdleClock 可控时钟） ----------------

    /// <summary>可控时钟（IdleClock 注入面驱动 — 值语义推进：CompleteTurn 锚点 + 阈值跨越）。</summary>
    private sealed class FakeClock
    {
        /// <summary>当前时间戳（ms）。</summary>
        public long Now;
    }

    /// <summary>构造注入空闲返回阈值的测试 shell（IdleClock 接 FakeClock）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine, FakeClock Clock) CreateShell(
        int thresholdMinutes = 75,
        long tokenThreshold = 100_000)
    {
        var engine = new FakeReplEngine();
        var clock = new FakeClock();
        var shell = new ReplShell(engine, new BannerItem("1", "m", "b", "/tmp"), new StringWriter(),
            idleReturnThresholdMinutes: thresholdMinutes,
            idleReturnTokenThreshold: tokenThreshold)
        {
            IdleClock = () => clock.Now
        };
        shell.SeedContent();
        return (shell, engine, clock);
    }

    /// <summary>注入回合完成（token 记账 + TurnCompleted 事件 — DrainEngine 刷新空闲锚点）。</summary>
    private static void CompleteTurnWithTokens(ReplShell shell, FakeReplEngine engine, long inputTokens)
    {
        engine.Events.Enqueue(new BridgeEvent.TokenUsage(inputTokens, 0));
        engine.Events.Enqueue(new BridgeEvent.TurnCompleted());
        shell.DrainEngine();
    }

    /// <summary>逐字符输入文本（编辑链 — 对话框未挂载时字符进输入框）。</summary>
    private static void Type(ReplShell shell, string text)
    {
        foreach (var ch in text)
        {
            shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, ch.ToString()));
        }
    }

    /// <summary>空转一拍（模态外按键驱动 — 对话框交互走 HandleRawKeyPress 模态链）。</summary>
    private static void Press(ReplShell shell, KeyKind kind) =>
        shell.HandleRawKeyPress(new KeyPress(kind, string.Empty));

    /// <summary>阈值内提交 — 不弹对话框，闲时直发（基准 idleMinutes &lt; idleThresholdMin 分支）。</summary>
    [Fact]
    public void Submit_WithinThreshold_SendsDirectly_NoDialog()
    {
        var (shell, engine, clock) = CreateShell();
        CompleteTurnWithTokens(shell, engine, 100_000);
        clock.Now += 74 * 60_000;

        Type(shell, "继续工作");
        shell.Submit();

        Assert.False(shell.IsIdleReturnOpen);
        Assert.Equal(["继续工作"], engine.Submits);
    }

    /// <summary>首回合提交 — 无完成回合锚点（lastQueryCompletionTime &gt; 0 门槛）不弹直接发。</summary>
    [Fact]
    public void Submit_FirstTurn_BeyondThreshold_NoDialog()
    {
        var (shell, engine, clock) = CreateShell();
        // token 已达门槛但未注入 TurnCompleted —— _lastTurnCompletionMs 仍为 0
        engine.Events.Enqueue(new BridgeEvent.TokenUsage(100_000, 0));
        shell.DrainEngine();
        clock.Now += 200 * 60_000;

        Type(shell, "首回合");
        shell.Submit();

        Assert.False(shell.IsIdleReturnOpen);
        Assert.Equal(["首回合"], engine.Submits);
    }

    /// <summary>token 门槛不足 — 空闲超阈值仍不弹（基准 CLAUDE_CODE_IDLE_TOKEN_THRESHOLD 小对话豁免）。</summary>
    [Fact]
    public void Submit_BelowTokenThreshold_BeyondIdle_NoDialog()
    {
        var (shell, engine, clock) = CreateShell();
        CompleteTurnWithTokens(shell, engine, 99_999);
        clock.Now += 200 * 60_000;

        Type(shell, "小对话");
        shell.Submit();

        Assert.False(shell.IsIdleReturnOpen);
        Assert.Equal(["小对话"], engine.Submits);
    }

    /// <summary>斜杠命令 — 空闲超阈值不弹（基准 !input.trim().startsWith('/') 豁免）。</summary>
    [Fact]
    public void Submit_SlashCommand_BeyondThreshold_NoDialog()
    {
        var (shell, engine, clock) = CreateShell();
        CompleteTurnWithTokens(shell, engine, 100_000);
        clock.Now += 200 * 60_000;

        Type(shell, "/help");
        shell.Submit();

        Assert.False(shell.IsIdleReturnOpen);
        Assert.Equal(["/help"], engine.Submits);
    }

    /// <summary>超阈值提交 — 拦截挂载对话框：不调引擎 + 输入清空 + 滚动区挂载 +
    /// 标题快照（idleMinutes 差值 + 累计输入 token）。</summary>
    [Fact]
    public void Submit_BeyondThreshold_MountsDialogAndIntercepts()
    {
        var (shell, engine, clock) = CreateShell();
        CompleteTurnWithTokens(shell, engine, 100_000);
        clock.Now += 76 * 60_000;

        Type(shell, "新任务");
        shell.Submit();

        Assert.True(shell.IsIdleReturnOpen);
        Assert.Empty(engine.Submits);
        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.Contains(shell.View.Messages.Items, item => item is IdleReturnDialog);
        Assert.NotNull(shell.IdleReturnDialog);
        Assert.Null(shell.IdleReturnDialog!.SelectedAction);
        Assert.Equal("你已离开 1h 16m，此对话已 100k tokens。", shell.IdleReturnDialog!.FormattedTitle);
    }

    /// <summary>esc 取消 — 恢复输入框文本（基准 dismiss → setInputValue 语义）+ 丢弃提交 + 对话框移除。</summary>
    [Fact]
    public void Submit_BeyondThreshold_Escape_RestoresInputAndDropsSubmission()
    {
        var (shell, engine, clock) = CreateShell();
        CompleteTurnWithTokens(shell, engine, 100_000);
        clock.Now += 76 * 60_000;
        Type(shell, "新任务");
        shell.Submit();

        Press(shell, KeyKind.Escape);

        Assert.False(shell.IsIdleReturnOpen);
        Assert.DoesNotContain(shell.View.Messages.Items, item => item is IdleReturnDialog);
        Assert.Empty(engine.Submits);
        Assert.Equal("新任务", shell.Input.Text);
    }

    /// <summary>enter 确认 Continue — 暂存提交原样重发（引擎收到 + 用户消息上屏）。</summary>
    [Fact]
    public void Submit_BeyondThreshold_Enter_Continue_ResumesSubmission()
    {
        var (shell, engine, clock) = CreateShell();
        CompleteTurnWithTokens(shell, engine, 100_000);
        clock.Now += 76 * 60_000;
        Type(shell, "新任务");
        shell.Submit();

        Press(shell, KeyKind.Return);

        Assert.False(shell.IsIdleReturnOpen);
        Assert.Equal(["新任务"], engine.Submits);
        Assert.Contains(shell.View.Messages.Items, m => m is UserMessageItem u && u.Text == "新任务");
    }

    /// <summary>enter 确认 Clear — G3 新会话链路：engine.StartNewConversation（引擎上下文清理）+
    /// 滚动区清空（banner/spinner 之外的旧消息全清）+ token 记账归零（footer 引用共享原地重置）+
    /// 暂存提交重发（D8 基准 "Send message as a new conversation" 完整语义）。</summary>
    [Fact]
    public void Submit_BeyondThreshold_Enter_Clear_StartsNewConversationAndResends()
    {
        var (shell, engine, clock) = CreateShell();
        CompleteTurnWithTokens(shell, engine, 100_000);
        clock.Now += 76 * 60_000;
        Type(shell, "旧任务");
        shell.Submit();
        Press(shell, KeyKind.Return);
        Assert.Equal(["旧任务"], engine.Submits);
        // 上屏一条历史消息（Clear 后滚动区清空断言的对照基数）
        Assert.Contains(shell.View.Messages.Items, m => m is UserMessageItem u && u.Text == "旧任务");

        clock.Now += 76 * 60_000;
        Type(shell, "新任务");
        shell.Submit();

        Press(shell, KeyKind.DownArrow);
        Press(shell, KeyKind.Return);

        Assert.False(shell.IsIdleReturnOpen);
        // G3 — 引擎新会话调用恰一次 + 暂存提交重发
        Assert.Equal(1, engine.NewConversations);
        Assert.Equal(["旧任务", "新任务"], engine.Submits);
        // D8 — 滚动区清空：旧消息清除（banner/spinner 不变式由 ClearMessagesExceptBannerAndSpinner 保）
        Assert.DoesNotContain(shell.View.Messages.Items, m => m is UserMessageItem u && u.Text == "旧任务");
        Assert.Contains(shell.View.Messages.Items, m => m is UserMessageItem u && u.Text == "新任务");
        // D8 — token 记账归零（100k 累计被 Reset，footer 引用共享原地重置）
        Assert.Equal(0, shell.View.Footer.Tokens!.InputTokens);
    }

    /// <summary>enter 确认 Continue — 不触发新会话（G3 零破坏对照：仅 Clear 动作走引擎新会话）。</summary>
    [Fact]
    public void Submit_BeyondThreshold_Enter_Continue_DoesNotStartNewConversation()
    {
        var (shell, engine, clock) = CreateShell();
        CompleteTurnWithTokens(shell, engine, 100_000);
        clock.Now += 76 * 60_000;
        Type(shell, "新任务");
        shell.Submit();

        Press(shell, KeyKind.Return);

        Assert.Equal(0, engine.NewConversations);
        Assert.Equal(["新任务"], engine.Submits);
    }

    /// <summary>enter 确认 Never — 置会话级持久标志 + 原样重发；此后超阈值提交不再弹直接发。</summary>
    [Fact]
    public void Submit_BeyondThreshold_Enter_Never_NeverPromptsAgain()
    {
        var (shell, engine, clock) = CreateShell();
        CompleteTurnWithTokens(shell, engine, 100_000);
        clock.Now += 76 * 60_000;
        Type(shell, "新任务");
        shell.Submit();

        Press(shell, KeyKind.DownArrow);
        Press(shell, KeyKind.DownArrow);
        Press(shell, KeyKind.Return);

        Assert.True(shell.IdleReturnNever);
        Assert.Equal(["新任务"], engine.Submits);

        clock.Now += 100 * 60_000;
        Type(shell, "下一个任务");
        shell.Submit();

        Assert.False(shell.IsIdleReturnOpen);
        Assert.Equal(["新任务", "下一个任务"], engine.Submits);
    }
}
