namespace NativeTui.Tests.Repl;

/// <summary>
/// 注入上下文折叠项测试（TASK079 用户指令 — 首次注入的提示词/工具清单在 TUI 可见可查看）：
/// 塌缩摘要行渲染（统计提示 + tokenNote 回填）/ HitTestId 命中标识 / 单行测量。
/// 点击→详情页打开的分派在 ReplShell 域（PromptTransparencyEngineTests 走 ShowPromptPage 全链路）。
/// </summary>
public sealed class PromptContextItemTests
{
    private static string RowTextOf(Screen screen, int row, int width = 140) =>
        RowText.Of(screen, row, 0, width).TrimEnd();

    [Fact]
    public void Collapsed_RendersSummaryLineWithPromptChars()
    {
        var item = new PromptContextItem(
            "📎 首次注入 · 系统提示词 50382 字符 · 点击查看（含工具清单）", 50382);
        using var screen = new Screen(cols: 140, rows: 3);

        Assert.Equal(1, item.MeasureHeight(140));
        item.Render(screen, new Rect(0, 0, 140, 3), Theme.Dark);

        var row = RowTextOf(screen, 0);
        Assert.Contains("📎 首次注入", row);
        Assert.Contains("50382 字符", row);
        Assert.Contains("点击查看", row);
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
    public void HitTestId_IsPromptContextConstant()
    {
        var item = new PromptContextItem("📎 首次注入", 10);
        Assert.Equal("prompt-context", item.HitTestId);
        Assert.Equal(PromptContextItem.HitIdValue, item.HitTestId);
    }
}
