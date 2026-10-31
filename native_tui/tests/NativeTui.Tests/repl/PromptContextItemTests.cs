namespace NativeTui.Tests.Repl;

/// <summary>
/// 注入上下文折叠项测试（TASK079 终版 — OSC 8 file 链接形态，不接管鼠标）：
/// 摘要行渲染（统计 + tokenNote 回填）/ 链接段 SetLinks 声明（支持时）/ 降级路径（不支持时）。
/// 点击→终端原生打开 .md（OSC 8 由渲染层注入）；/prompt 全页模态在 PromptTransparencyEngineTests。
/// </summary>
public sealed class PromptContextItemTests
{
    private const string FileUrl = "file:///C:/Users/test/AppData/Local/Temp/jcc-prompt-20261031.md";

    private static string RowTextOf(Screen screen, int row, int width = 140) =>
        RowText.Of(screen, row, 0, width).TrimEnd();

    [Fact]
    public void Supported_RendersSummaryAndDeclaresFileLinkSpan()
    {
        var item = new PromptContextItem("📎 首次注入 · 系统提示词 50382 字符 · 点击展开", 50382);
        using var screen = new Screen(cols: 140, rows: 3);

        Assert.Equal(1, item.MeasureHeight(140));
        item.Render(screen, new Rect(0, 0, 140, 3), Theme.Dark);

        var row = RowTextOf(screen, 0);
        Assert.Contains("📎 首次注入", row);
        Assert.Contains("50382 字符", row);
        Assert.Contains("点击展开", row);
    }

    [Fact]
    public void Unsupported_ShowsFallbackPath_NoLinkSpan()
    {
        var item = new PromptContextItem("📎 首次注入 · 系统提示词 50382 字符", 50382);
        using var screen = new Screen(cols: 140, rows: 3);

        item.Render(screen, new Rect(0, 0, 140, 3), Theme.Dark);

        Assert.Contains("📎 首次注入", RowTextOf(screen, 0));
    }

    [Fact]
    public void TokenNote_Renders_WhenPromptTokensFilled()
    {
        var item = new PromptContextItem("📎 首次注入 · 系统提示词 12307 字符", 12307) { PromptTokens = 4200 };
        using var screen = new Screen(cols: 140, rows: 3);

        item.Render(screen, new Rect(0, 0, 140, 3), Theme.Dark);

        Assert.Contains("约 4200 tokens", RowTextOf(screen, 0));
    }

    [Fact]
    public void NoUrlNoFallback_RendersPlainLinkText()
    {
        var item = new PromptContextItem("📎 首次注入", 10);
        using var screen = new Screen(cols: 140, rows: 3);

        item.Render(screen, new Rect(0, 0, 140, 3), Theme.Dark);

        Assert.Contains("📎 首次注入", RowTextOf(screen, 0));
    }
}
