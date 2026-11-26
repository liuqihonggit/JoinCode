namespace NativeTui.Tests.Repl;

/// <summary>
/// 工具结果摘要渲染门禁 — 真机现象（TASK107）：bash 结果的 resultText 是多行输出，
/// 引擎按前 200 字符原样下发展开摘要，TUI 单次 WriteText 写屏 → 换行被零宽规则跳过，
/// 四行输出连同元数据表挤成一行（`⎿ 工具调用执行成功AGENTS.mdartifactsbuild| ExitCode | ...`）。
/// 门禁锁：摘要只渲染首个非空行、高度恒 1 行（margin 外）、多行内容让位给 rawOutput 折叠块；
/// 原文仍完整保留在 Summary（搜索/复制/导出断言面不受影响）。
/// </summary>
public sealed class ToolResultSummaryRenderTests
{
    [Fact]
    public void Render_MultilineSummary_RendersFirstLineOnly()
    {
        using var screen = new Screen(cols: 80, rows: 6);
        var item = new ToolResultItem("工具调用执行成功\nAGENTS.md\nartifacts\nbuild");

        Assert.Equal(2, item.MeasureHeight(80));
        item.Render(screen, new Rect(0, 0, 80, 6), Theme.Dark);

        var summaryRow = RowText.Of(screen, 1, 0, 80);
        Assert.Contains("工具调用执行成功", summaryRow);
        Assert.DoesNotContain("AGENTS.md", summaryRow);
        Assert.DoesNotContain("artifacts", summaryRow);
        // 第二行（margin+1 之后）不得被摘要占用
        Assert.DoesNotContain("AGENTS.md", RowText.Of(screen, 2, 0, 80));
    }

    [Fact]
    public void Render_CrLfSummary_NoCarriageReturnOnScreen()
    {
        using var screen = new Screen(cols: 80, rows: 4);
        var item = new ToolResultItem("行一\r\n行二");

        item.Render(screen, new Rect(0, 0, 80, 4), Theme.Dark);

        Assert.DoesNotContain('\r', RowText.Of(screen, 1, 0, 80));
    }

    [Fact]
    public void Render_LeadingBlankLines_SkipsToFirstContentLine()
    {
        using var screen = new Screen(cols: 80, rows: 4);
        var item = new ToolResultItem("\n\n真正结果");

        item.Render(screen, new Rect(0, 0, 80, 4), Theme.Dark);

        Assert.Contains("真正结果", RowText.Of(screen, 1, 0, 80));
    }

    [Fact]
    public void Render_SingleLineSummary_Unchanged()
    {
        using var screen = new Screen(cols: 80, rows: 4);
        var item = new ToolResultItem("Build succeeded");

        item.Render(screen, new Rect(0, 0, 80, 4), Theme.Dark);

        Assert.Contains("Build succeeded", RowText.Of(screen, 1, 0, 80));
    }

    [Fact]
    public void Summary_RawTextPreservedForSearch()
    {
        var item = new ToolResultItem("首行\n第二行内容");

        Assert.Equal("首行\n第二行内容", item.Summary);
        Assert.True(item.ContainsText("第二行"));
    }

    [Fact]
    public void Render_SummaryEqualsFirstOutputLine_RendersItOnce()
    {
        // 引擎 summary 取自结果文本前 200 字符 → 与 rawOutput 首行天然相同；
        // 旧实现两处都画导致首行重复。门禁锁：整屏只出现一次。
        using var screen = new Screen(cols: 80, rows: 10);
        var item = new ToolResultItem(
            "工具调用执行成功\nAGENTS.md\nartifacts\nbuild\ntools",
            patch: null,
            rawOutput: "工具调用执行成功\nAGENTS.md\nartifacts\nbuild\ntools",
            hiddenLines: 2);

        item.Render(screen, new Rect(0, 0, 80, 10), Theme.Dark);

        var all = string.Concat(Enumerable.Range(0, 10).Select(row => RowText.Of(screen, row, 0, 80) + "\n"));
        Assert.Equal(1, CountOccurrences(all, "工具调用执行成功"));
    }

    /// <summary>统计子串出现次数。</summary>
    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
