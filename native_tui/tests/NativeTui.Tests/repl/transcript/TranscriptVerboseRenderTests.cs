namespace NativeTui.Tests.Repl;

/// <summary>
/// transcript（ctrl+o 详细转录）投影门禁 — TASK107 真机在详情页发现三类粘连/无边界问题：
/// 多行摘要被拼成一行、段内软换行丢失、CoT 与正文同为裸文本无法分辨。
/// 门禁锁：摘要只投影首个非空行；段内 "\n" 拆行；引用块带 ▎；CoT 每行带 ∴。
/// </summary>
public sealed class TranscriptVerboseRenderTests
{
    [Fact]
    public void ToolResult_MultilineSummary_ProjectsFirstLineOnly()
    {
        var item = new ToolResultItem("工具调用执行成功\nAGENTS.md\nartifacts\nbuild");

        var lines = TranscriptLines.Project(item).ToArray();

        var summary = Assert.Single(lines);
        Assert.Contains("工具调用执行成功", summary);
        Assert.DoesNotContain("AGENTS.md", summary);
    }

    [Fact]
    public void ToolResult_SummaryEqualsFirstOutputLine_ProjectedOnce()
    {
        // 详情页 = 摘要行 + 输出全量；首行同源时不得连续出现两次
        var item = new ToolResultItem(
            "工具调用执行成功\nAGENTS.md\nartifacts",
            patch: null,
            rawOutput: "工具调用执行成功\nAGENTS.md\nartifacts",
            hiddenLines: 0);

        var lines = TranscriptLines.Project(item).ToArray();

        Assert.Equal(1, lines.Count(line => line.Contains("工具调用执行成功", StringComparison.Ordinal)));
        Assert.Contains(lines, line => line.EndsWith("AGENTS.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Markdown_ParagraphSoftBreak_ProjectsTwoLines()
    {
        var document = new MarkdownDocument([
            new MarkdownParagraph([new MarkdownSegment("第一行\n第二行", MarkdownStyle.None)]),
        ]);

        var lines = TranscriptLines.Project(new MarkdownMessageItem(document)).ToArray();

        // 首行带 ● 前缀，续行为裸行 —— 断言"拆成两行"而不是"拼成一行"
        Assert.Contains(lines, line => line.EndsWith("第一行", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EndsWith("第二行", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("第一行第二行", StringComparison.Ordinal));
    }

    [Fact]
    public void Markdown_Quote_SignsEachLineWithBar()
    {
        var document = new MarkdownDocument([
            new MarkdownQuote([new MarkdownParagraph([new MarkdownSegment("第一行\n第二行", MarkdownStyle.None)])]),
        ]);

        var lines = TranscriptLines.Project(new MarkdownMessageItem(document)).ToArray();

        Assert.Contains(lines, line => line.EndsWith("▎ 第一行", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EndsWith("▎ 第二行", StringComparison.Ordinal));
    }

    [Fact]
    public void Markdown_Table_ProjectsBoxBorders()
    {
        // TASK107 P7 — 详情页表格曾按 GFM 源形态（"a | b"）拍平，与主屏 box 边框不一致
        var document = new MarkdownDocument([
            new MarkdownTable(
                [Cell("元素"), Cell("期望")],
                [[Cell("标题"), Cell("待验")]]),
        ]);

        var lines = TranscriptLines.Project(new MarkdownMessageItem(document)).ToArray();

        Assert.Contains(lines, line => line.Contains('┌') && line.Contains('┬') && line.Contains('┐'));
        Assert.Contains(lines, line => line.Contains('└') && line.Contains('┴') && line.Contains('┘'));
        Assert.Contains(lines, line => line.Contains('│') && line.Contains("标题"));
        // GFM 分隔行不得上屏
        Assert.DoesNotContain(lines, line => line.Contains("|---|", StringComparison.Ordinal));
    }

    [Fact]
    public void Markdown_NestedList_ProjectsNestedLine()
    {
        // TASK107 P8 — 嵌套子项曾被拍平拼进父项同一行（"- 第二项 - 嵌套子项"）
        var nested = new MarkdownList(false, 1, [
            new MarkdownListItem([new MarkdownParagraph([new MarkdownSegment("子项", MarkdownStyle.None)])]),
        ]);
        var document = new MarkdownDocument([
            new MarkdownList(false, 1, [
                new MarkdownListItem([new MarkdownParagraph([new MarkdownSegment("第二项", MarkdownStyle.None)])]),
                new MarkdownListItem([
                    new MarkdownParagraph([new MarkdownSegment("父项", MarkdownStyle.None)]),
                    nested,
                ]),
            ]),
        ]);

        var lines = TranscriptLines.Project(new MarkdownMessageItem(document)).ToArray();

        Assert.Contains(lines, line => line.EndsWith("第二项", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EndsWith("父项", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("父项 子项", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("父项 - 子项", StringComparison.Ordinal));
    }

    [Fact]
    public void ToolResult_OutputFirstLine_NotPrefixedTwice()
    {
        // TASK107 P9 — 摘要行已带 "⎿"，输出块首行又带一次 → 详情页连续两行 "⎿"
        var item = new ToolResultItem(
            "工具调用执行成功\nAGENTS.md",
            patch: null,
            rawOutput: "工具调用执行成功\nAGENTS.md",
            hiddenLines: 0);

        var lines = TranscriptLines.Project(item).ToArray();

        Assert.Equal(1, lines.Count(line => line.StartsWith(Figures.ToolResultIndent, StringComparison.Ordinal)));
        Assert.Contains(lines, line => line.EndsWith("AGENTS.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Assistant_ThinkingContent_SignsEachLineWithTherefore()
    {
        var item = new AssistantMessageItem("正文", thinkingDurationMs: 1000, thinkingContent: "想第一步\n想第二步");

        var lines = TranscriptLines.Project(item).ToArray();

        Assert.Contains("∴ 想第一步", lines);
        Assert.Contains("∴ 想第二步", lines);
    }

    [Fact]
    public void ToolCall_Title_MatchesMainScreenForm()
    {
        // TASK107 P11 — 详情页工具标题曾缺括号（"● Bash echo hi"），主屏是 "● Bash(echo hi)"
        var item = new ToolCallItem("bash", "{\"command\":\"echo hi\"}");

        var lines = TranscriptLines.Project(item).ToArray();

        Assert.Equal("● Bash(echo hi)", lines[0]);
    }

    [Fact]
    public void ToolCall_MultilineArgs_ClosesParenOnLastLine()
    {
        var item = new ToolCallItem("bash", "{\"command\":\"line1\\nline2\"}");

        var lines = TranscriptLines.Project(item).ToArray();

        Assert.Equal("● Bash(line1", lines[0]);
        Assert.Equal("  line2)", lines[^1]);
    }

    [Fact]
    public void PromptContext_CollapsedSummary_NotPrefixedTwice()
    {
        // TASK107 P10 — Summary 自带 📎，投影层再补一次 → "📎 📎 第 1 轮注入…"
        var item = new PromptContextItem(round: 1, promptChars: 87432, content: "提示词正文");

        var lines = TranscriptLines.Project(item).ToArray();

        var summary = Assert.Single(lines);
        Assert.Equal("📎 第 1 轮注入 · 提示词 87432 字符", summary);
    }

    [Fact]
    public void ToolResult_PatchLines_CarryGutterLineNumbers()
    {
        // TASK108 P16 — 详情页 diff 曾丢行号（主屏有 gutter，详情页只有 +/- 前缀），
        // 改文件后进 ctrl+o 无法定位到具体行。行号列与主屏共用 DiffGutter。
        var patch = new[]
        {
            new DiffHunk
            {
                Header = "@@ -1,2 +1,2 @@",
                Lines =
                [
                    new DiffLine { Kind = DiffLineKind.Context, Content = "第一行" },
                    new DiffLine { Kind = DiffLineKind.Removed, Content = "旧" },
                    new DiffLine { Kind = DiffLineKind.Added, Content = "新" },
                ],
            },
        };
        var item = new ToolResultItem("已更新", patch, null, hiddenLines: 0);

        var lines = TranscriptLines.Project(item).ToArray();

        Assert.Contains("    @@ -1,2 +1,2 @@", lines);
        Assert.Contains("     1  第一行", lines);
        Assert.Contains("     2 -旧", lines);
        Assert.Contains("     2 +新", lines);
    }

    /// <summary>单元格段序列（纯文本一段）。</summary>
    private static IReadOnlyList<MarkdownSegment> Cell(string text) => [new MarkdownSegment(text, MarkdownStyle.None)];
}
