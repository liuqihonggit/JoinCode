namespace NativeTui.Tests.Repl;

/// <summary>
/// 会话导出格式化器测试（Export 对话框域）— 基准 commands/export/export.tsx content 组装语义：
/// 四形态段组装（用户 "## 用户" / 助手 "## 助手" / 工具调用 "&gt; 工具 {名}" + 主参数 /
/// 工具结果逐行 "&gt; " 缩进引用块 / 错误 "**错误**" 加粗标记）+ 段间空行 join +
/// 空会话空串 + 不可导出项跳过 + 提取面委托 CopyTextExtractor（system-reminder 剥离 /
/// Markdown 拍平归助手段 / 主参数容错解析）。
/// </summary>
public sealed class ExportTranscriptExporterTests
{
    /// <summary>装饰项假桩（非已知消息类型 — CopyTextExtractor 返回 null，导出零产出断言用）。</summary>
    private sealed class FakeDecorativeItem : IScrollItem
    {
        /// <summary>内容高度（与 banner 同形态 — 本测试不消费渲染）。</summary>
        public int MeasureHeight(int width) => 1;

        /// <summary>不渲染（导出逻辑纯数据面）。</summary>
        public void Render(Screen screen, Rect frame, Theme theme)
        {
        }
    }

    /// <summary>空会话 — 空串（无项零产出）。</summary>
    [Fact]
    public void Export_EmptyConversation_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, TranscriptExporter.Export([]));
    }

    /// <summary>用户消息段 — "## 用户" 标题 + 空行 + 提取正文。</summary>
    [Fact]
    public void Export_UserMessage_HeadingBlock()
    {
        Assert.Equal("## 用户\n\n帮我看下报错", TranscriptExporter.Export([new UserMessageItem("帮我看下报错")]));
    }

    /// <summary>用户消息 system-reminder 剥离 — 委托 CopyTextExtractor 提取面（零重复实现）。</summary>
    [Fact]
    public void Export_UserMessage_StripsSystemReminders()
    {
        var result = TranscriptExporter.Export([new UserMessageItem("<system-reminder>env</system-reminder>正文")]);
        Assert.Equal("## 用户\n\n正文", result);
    }

    /// <summary>助手消息段 — "## 助手" 标题 + 空行 + 正文。</summary>
    [Fact]
    public void Export_AssistantMessage_HeadingBlock()
    {
        Assert.Equal("## 助手\n\n回答正文", TranscriptExporter.Export([new AssistantMessageItem("回答正文")]));
    }

    /// <summary>Markdown 消息归助手段 — 块模型拍平文本作为助手导出面（助手侧渲染产物的对等）。</summary>
    [Fact]
    public void Export_MarkdownMessage_GroupsIntoAssistantSection()
    {
        var document = new MarkdownDocument(
        [
            new MarkdownParagraph([new MarkdownSegment("段落", MarkdownStyle.None)]),
            new MarkdownCode("csharp", "Console.WriteLine(1);"),
        ]);
        var result = TranscriptExporter.Export([new MarkdownMessageItem(document)]);

        Assert.Equal("## 助手\n\n段落\n\nConsole.WriteLine(1);", result);
    }

    /// <summary>工具调用段 — "&gt; 工具 {名}" 引用行 + 空行 + 主参数原文（PrimaryInputMap 提取）。</summary>
    [Fact]
    public void Export_ToolCall_QuoteLineAndPrimaryInput()
    {
        var result = TranscriptExporter.Export([new ToolCallItem("Bash", "{\"command\":\"dotnet build\"}")]);

        Assert.Equal("> 工具 Bash\n\ndotnet build", result);
    }

    /// <summary>工具调用主参数不可提取（未映射工具）— 仅保留引用行，工具调用发生记录不丢失。</summary>
    [Fact]
    public void Export_ToolCall_UnmappedTool_KeepsQuoteLineOnly()
    {
        var result = TranscriptExporter.Export([new ToolCallItem("CustomTool", "{\"a\":1}")]);

        Assert.Equal("> 工具 CustomTool", result);
    }

    /// <summary>工具结果段 — 逐行 "&gt; " 缩进引用块（多行结果保持行结构）。</summary>
    [Fact]
    public void Export_ToolResult_IndentedQuoteBlock()
    {
        var result = TranscriptExporter.Export([new ToolResultItem("Build succeeded\n0 warning")]);

        Assert.Equal("> Build succeeded\n> 0 warning", result);
    }

    /// <summary>工具结果空行 — 裸 "&gt;" 防尾随空格（Markdown 引用块空行形态）。</summary>
    [Fact]
    public void Export_ToolResult_EmptyLineBareQuote()
    {
        var result = TranscriptExporter.Export([new ToolResultItem("行1\n\n行2")]);

        Assert.Equal("> 行1\n>\n> 行2", result);
    }

    /// <summary>diff 结果段 — 提取面拼接文本整体走引用块（工具结果形态归类）。</summary>
    [Fact]
    public void Export_DiffResult_QuoteBlock()
    {
        var diff = new DiffResultItem("改 2 处",
        [
            new DiffHunk
            {
                Header = "@@ -1,2 +1,3 @@",
                Lines =
                [
                    new DiffLine { Kind = DiffLineKind.Context, Content = "context" },
                ],
            },
        ]);
        var result = TranscriptExporter.Export([diff]);

        Assert.Equal("> 改 2 处\n> @@ -1,2 +1,3 @@\n>  context", result);
    }

    /// <summary>错误段 — "**错误**" 加粗标记 + 空行 + 正文（纯文本 Markdown 标红以加粗对等）。</summary>
    [Fact]
    public void Export_Error_BoldHeadingBlock()
    {
        Assert.Equal("**错误**\n\nboom", TranscriptExporter.Export([new ErrorMessageItem("boom")]));
    }

    /// <summary>四形态混合会话 — 段间空行 join + 会话顺序保持。</summary>
    [Fact]
    public void Export_MixedConversation_JoinsSectionsWithBlankLine()
    {
        var items = new IScrollItem[]
        {
            new UserMessageItem("帮我看下报错"),
            new AssistantMessageItem("我先查一下"),
            new ToolCallItem("Bash", "{\"command\":\"dotnet build\"}"),
            new ToolResultItem("Build succeeded"),
            new ErrorMessageItem("boom"),
        };
        var expected = "## 用户\n\n帮我看下报错"
            + "\n\n## 助手\n\n我先查一下"
            + "\n\n> 工具 Bash\n\ndotnet build"
            + "\n\n> Build succeeded"
            + "\n\n**错误**\n\nboom";

        Assert.Equal(expected, TranscriptExporter.Export(items));
    }

    /// <summary>不可导出项跳过 — banner + 未知装饰桩混合空正文消息 → 全零产出空串。</summary>
    [Fact]
    public void Export_UnextractableItems_Skipped()
    {
        var items = new IScrollItem[]
        {
            new BannerItem("1.0.0", "m", "API", "/tmp"),
            new FakeDecorativeItem(),
            new ToolResultItem(string.Empty),
            new AssistantMessageItem(string.Empty),
        };

        Assert.Equal(string.Empty, TranscriptExporter.Export(items));
    }

    /// <summary>空正文用户消息段跳过 — HeadingBlock 空段过滤（空消息无导出价值）。</summary>
    [Fact]
    public void Export_EmptyTextUserMessage_SectionSkipped()
    {
        var items = new IScrollItem[] { new UserMessageItem(string.Empty), new AssistantMessageItem("回答") };

        Assert.Equal("## 助手\n\n回答", TranscriptExporter.Export(items));
    }
}
