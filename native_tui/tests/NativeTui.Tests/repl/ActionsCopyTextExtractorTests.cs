namespace NativeTui.Tests.Repl;

/// <summary>
/// 复制全文提取器测试（消息操作域）— 对齐基准 messageActions.tsx copyTextOf/stripSystemReminders：
/// 按项类型提取（用户消息剥 system-reminder / assistant 纯文本 / Markdown 拍平 / 工具结果与错误正文 /
/// diff 拼接 / 子代理分组拼接 / 工具调用主参数 — 基准 assistant tool_use 分支）；不可复制类型 null。
/// ToolCallItem 的 IToolCallInfo 接线在 item 域（S2，见未决问题）— 本文件以 FakeToolCall 验证提取逻辑。
/// </summary>
public sealed class ActionsCopyTextExtractorTests
{
    /// <summary>工具调用假项（IToolCallInfo 契约 — ToolCallItem 接线前的提取逻辑验证载体）。</summary>
    private sealed record FakeToolCall(string ToolName, string Args) : IScrollItem, IToolCallInfo
    {
        /// <summary>内容高度（与 ToolCallItem 同形态 — 本测试不消费）。</summary>
        public int MeasureHeight(int width) => 2;

        /// <summary>不渲染（提取逻辑纯数据面）。</summary>
        public void Render(Screen screen, Rect frame, Theme theme)
        {
        }
    }

    /// <summary>StripSystemReminders — 剥离全文 system-reminder 块 + 首尾去空白（本项目推广覆盖中部场景）。</summary>
    [Fact]
    public void StripSystemReminders_RemovesBlocksEverywhere()
    {
        Assert.Equal("正文", CopyTextExtractor.StripSystemReminders("<system-reminder>提示</system-reminder>正文"));
        Assert.Equal("头部 正文 尾部", CopyTextExtractor.StripSystemReminders(
            " 头部 <system-reminder>\n多行\n提示\n</system-reminder> 正文 <system-reminder>x</system-reminder> 尾部 "));
        Assert.Equal("无块原样", CopyTextExtractor.StripSystemReminders("无块原样"));
        Assert.Equal(string.Empty, CopyTextExtractor.StripSystemReminders(string.Empty));
    }

    /// <summary>用户消息 — 提取剥离 system-reminder 后的原文（基准 user 分支）。</summary>
    [Fact]
    public void Extract_UserMessage_StripsSystemReminders()
    {
        var item = new UserMessageItem("<system-reminder>env</system-reminder>帮我看下报错");
        Assert.Equal("帮我看下报错", CopyTextExtractor.Extract(item));
    }

    /// <summary>assistant 纯文本 — 提取原文。</summary>
    [Fact]
    public void Extract_AssistantMessage_ReturnsText()
    {
        Assert.Equal("回答正文", CopyTextExtractor.Extract(new AssistantMessageItem("回答正文")));
    }

    /// <summary>Markdown 消息 — 块模型拍平纯文本（段落/代码/列表/表格，块间空行）。</summary>
    [Fact]
    public void Extract_MarkdownMessage_FlattensToPlainText()
    {
        var document = new MarkdownDocument(
        [
            new MarkdownParagraph([new MarkdownSegment("段落", MarkdownStyle.None)]),
            new MarkdownCode("csharp", "Console.WriteLine(1);"),
            new MarkdownList(Ordered: false, Start: 0,
            [
                new MarkdownListItem([new MarkdownParagraph([new MarkdownSegment("项一", MarkdownStyle.None)])]),
                new MarkdownListItem([new MarkdownParagraph([new MarkdownSegment("项二", MarkdownStyle.Bold)])]),
            ]),
        ]);
        var expected = "段落\n\nConsole.WriteLine(1);\n\n- 项一\n- 项二";
        Assert.Equal(expected, CopyTextExtractor.Extract(new MarkdownMessageItem(document)));
    }

    /// <summary>Markdown 表格 — 单元格段拼接、列间 "| "。</summary>
    [Fact]
    public void Extract_MarkdownTable_PipesBetweenCells()
    {
        var table = new MarkdownTable(
            Headers: [[new MarkdownSegment("列A", MarkdownStyle.None)], [new MarkdownSegment("列B", MarkdownStyle.None)]],
            Rows: [[[new MarkdownSegment("1", MarkdownStyle.None)], [new MarkdownSegment("2", MarkdownStyle.None)]]]);
        var document = new MarkdownDocument([table]);
        Assert.Equal("列A | 列B\n1 | 2", CopyTextExtractor.Extract(new MarkdownMessageItem(document)));
    }

    /// <summary>工具结果/错误消息 — 提取正文。</summary>
    [Fact]
    public void Extract_ToolResultAndError_ReturnText()
    {
        Assert.Equal("42 lines", CopyTextExtractor.Extract(new ToolResultItem("42 lines")));
        Assert.Equal("boom", CopyTextExtractor.Extract(new ErrorMessageItem("boom")));
    }

    /// <summary>diff 结果 — summary + 逐 hunk header + +/-/空格前缀行拼接。</summary>
    [Fact]
    public void Extract_DiffResult_ConcatenatesSummaryAndPatch()
    {
        var diff = new DiffResultItem("改 2 处",
        [
            new DiffHunk
            {
                Header = "@@ -1,2 +1,3 @@",
                Lines =
                [
                    new DiffLine { Kind = DiffLineKind.Context, Content = "context" },
                    new DiffLine { Kind = DiffLineKind.Removed, Content = "old" },
                    new DiffLine { Kind = DiffLineKind.Added, Content = "new" },
                ],
            },
        ]);
        Assert.Equal("改 2 处\n@@ -1,2 +1,3 @@\n context\n-old\n+new", CopyTextExtractor.Extract(diff));
    }

    /// <summary>子代理分组 — 逐条目状态行 + 活动行拼接，条目间空行（基准 grouped join('\n\n') 语义）。</summary>
    [Fact]
    public void Extract_SubagentGroup_JoinsEntriesWithBlankLine()
    {
        var coordinator = new SubagentGroupCoordinator();
        coordinator.AddAgent("a1", "explore", "搜索");
        coordinator.AddAgent("a2", "builder", "编译");
        coordinator.RecordToolUse("a1", "Grep", "命中 3 处", 100);
        coordinator.FinishAgent("a2", isError: false, tokenTotal: 42);

        var expected = "explore (搜索) · 1 tool uses · 100 tokens\nGrep: 命中 3 处"
            + "\n\n"
            + "builder (编译) · 0 tool uses · 42 tokens";
        Assert.Equal(expected, CopyTextExtractor.Extract(new SubagentGroupItem(coordinator)));
    }

    /// <summary>工具调用（IToolCallInfo）— 提取主参数值（基准 assistant tool_use 分支）。</summary>
    [Fact]
    public void Extract_ToolCall_ReturnsPrimaryInput()
    {
        var item = new FakeToolCall("Bash", "{\"command\":\"dotnet build\"}");
        Assert.Equal("dotnet build", CopyTextExtractor.Extract(item));
    }

    /// <summary>装饰项（banner）— 不可复制返回 null。</summary>
    [Fact]
    public void Extract_BannerItem_ReturnsNull()
    {
        Assert.Null(CopyTextExtractor.Extract(new BannerItem("1.0.0", "m", "API", "/tmp")));
    }
}
