namespace NativeTui.Tests.Repl;

/// <summary>
/// 结构化消息测试（TASK062 — 消息 ID/思考时长/CoT 可展开）—
/// 数据链（AssistantMessage 三字段 → 消息项构造）、渲染（思考摘要行/展开 CoT）、
/// 交互（Ctrl+O 切换最近一条；无 CoT 静默）、组装（InProcessEventConsumer 思考累积+序号递增）。
/// </summary>
public sealed class StructuredMessageTests
{
    private const int FrameX = 0;

    // ---- 数据链 ----

    /// <summary>AssistantMessage 事件三字段 — 旧调用（仅 Text/Markdown）默认值零破坏。</summary>
    [Fact]
    public void AssistantMessageEvent_NewFields_DefaultBackwardCompatible()
    {
        var legacy = new BridgeEvent.AssistantMessage("hi");
        var rich = new BridgeEvent.AssistantMessage("hi", Markdown: null, MessageId: 3, ThinkingDurationMs: 3200, ThinkingContent: "思考过程");

        Assert.Equal(0, legacy.MessageId);
        Assert.Equal(0, legacy.ThinkingDurationMs);
        Assert.Null(legacy.ThinkingContent);
        Assert.Equal(3, rich.MessageId);
        Assert.Equal(3200, rich.ThinkingDurationMs);
        Assert.Equal("思考过程", rich.ThinkingContent);
    }

    /// <summary>driver 透传 — 事件字段进消息项（Markdown null 路径 AssistantMessageItem）。</summary>
    [Fact]
    public void Driver_AssistantMessageFields_ProjectToItem()
    {
        var view = new ReplView();
        var spinner = new SpinnerItem();
        var banner = new BannerItem("0.0.0", "m", "b", "C:\\");
        var driver = new ReplBridgeDriver(view, spinner, banner);
        view.Messages.Add(banner);
        view.Messages.Add(spinner);

        driver.Apply(new BridgeEvent.AssistantMessage("回复正文", Markdown: null, MessageId: 2, ThinkingDurationMs: 1500, ThinkingContent: "先分析再回答"));

        var item = view.Messages.Items.OfType<AssistantMessageItem>().Single();
        Assert.Equal(2, item.MessageId);
        Assert.Equal(1500, item.ThinkingDurationMs);
        Assert.Equal("先分析再回答", item.ThinkingContent);
    }

    // ---- 渲染 ----

    /// <summary>有思考 — 高度含思考摘要行；渲染含"思考"dim 行与正文。</summary>
    [Fact]
    public void Render_WithThinking_ShowsSummaryRowBeforeBody()
    {
        using var screen = new Screen(cols: 40, rows: 12);
        var item = new AssistantMessageItem("正文", thinkingDurationMs: 3200, thinkingContent: "CoT", messageId: 1);

        Assert.Equal(3, item.MeasureHeight(40));
        item.Render(screen, new Rect(FrameX, 2, 40, 3), Theme.Dark);

        var row1 = screen.RowText(3);
        Assert.Contains("思考", row1);
        Assert.Contains("ctrl+o", row1);
        var row2 = screen.RowText(4);
        Assert.Contains("正文", row2);
    }

    /// <summary>无思考 — 高度不变（2 行），无思考行渲染。</summary>
    [Fact]
    public void Render_WithoutThinking_NoSummaryRow()
    {
        using var screen = new Screen(cols: 40, rows: 12);
        var item = new AssistantMessageItem("正文");

        Assert.Equal(2, item.MeasureHeight(40));
        item.Render(screen, new Rect(FrameX, 2, 40, 2), Theme.Dark);

        Assert.DoesNotContain("思考", screen.RowText(3));
        Assert.Contains("正文", screen.RowText(3));
    }

    /// <summary>展开态 — CoT 全文渲染 + 高度增长；Toggle 来回收复。</summary>
    [Fact]
    public void Toggle_ExpandedCoT_RendersAndMeasures()
    {
        using var screen = new Screen(cols: 60, rows: 16);
        var item = new AssistantMessageItem("正文", thinkingDurationMs: 500, thinkingContent: "第一行推理\n第二行推理", messageId: 1);

        item.ToggleThinking();
        Assert.True(item.IsThinkingExpanded);
        Assert.Equal(2 + 1 + 2, item.MeasureHeight(60));
        item.Render(screen, new Rect(FrameX, 2, 60, 5), Theme.Dark);

        var combined = screen.RowText(3) + screen.RowText(4) + screen.RowText(5);
        Assert.Contains("第一行推理", combined);
        Assert.Contains("第二行推理", combined);
        Assert.Contains("▾", screen.RowText(3));

        item.ToggleThinking();
        Assert.False(item.IsThinkingExpanded);
        Assert.Equal(3, item.MeasureHeight(60));
    }

    /// <summary>无 CoT — Toggle 无操作（时长有但内容空不可展开）。</summary>
    [Fact]
    public void Toggle_NoContent_NoOp()
    {
        var item = new AssistantMessageItem("正文", thinkingDurationMs: 100);

        item.ToggleThinking();

        Assert.False(item.IsThinkingExpanded);
    }

    // ---- 交互（ReplShell Ctrl+O） ----

    /// <summary>Ctrl+O — 进入 transcript 全局模式（M8-3/S6 收敛语义演进：单条切换 → Controller
    /// 快照 verbose 行投影 + ReplView verbose 联动开 — CoT 全文进快照行）。</summary>
    [Fact]
    public void CtrlO_TogglesLastMessageWithCoT()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0", "m", "b", "C:\\"), new StringWriter(), sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("第一条", MessageId: 1, ThinkingDurationMs: 100, ThinkingContent: "CoT1"));
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("第二条", MessageId: 2, ThinkingDurationMs: 200, ThinkingContent: "CoT2"));
        shell.DrainEngine();

        shell.HandleKey(new ConsoleKeyInfo('\x0f', ConsoleKey.O, false, false, false));

        Assert.True(shell.IsTranscriptMode);
        Assert.True(shell.View.VerboseMode);
        Assert.Contains(shell.Transcript.Snapshot!.Lines, static line => line.Contains("CoT1"));
        Assert.Contains(shell.Transcript.Snapshot.Lines, static line => line.Contains("CoT2"));
    }

    /// <summary>Ctrl+O 无 CoT — 静默无操作（非思考模型路径不弹错误 — 快照仅正文行）。</summary>
    [Fact]
    public void CtrlO_NoCoTAnywhere_SilentNoOp()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0", "m", "b", "C:\\"), new StringWriter(), sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("无思考回复"));
        shell.DrainEngine();

        shell.HandleKey(new ConsoleKeyInfo('\x0f', ConsoleKey.O, false, false, false));

        Assert.True(shell.IsTranscriptMode);
        Assert.DoesNotContain(shell.Transcript.Snapshot!.Lines, static line => line.Contains("CoT"));
    }
}

/// <summary>Screen 行文本辅助（测试断言用 — 拼接该行字符；SpacerTail 跳过防宽字符双份）。</summary>
public static class ScreenTestExtensions
{
    public static string RowText(this Screen screen, int row) {
        var sb = new System.Text.StringBuilder();
        for (var col = 0; col < screen.Cols; col++) {
            var cell = screen[row, col];
            if (cell.Width == CellWidth.SpacerTail)
                continue;
            sb.Append(cell.Value.ToString());
        }
        return sb.ToString().TrimEnd();
    }
}
