namespace NativeTui.Tests.Repl;

/// <summary>
/// 注入上下文折叠项测试（TASK079 用户指令 — 首次注入的提示词/工具清单在 TUI 可见可展开，
/// 鼠标左键点击展开/收起）：塌缩摘要行渲染（统计 + 点击展开提示）/ 展开态快照全文 +
/// 点击收起 / 高度测量 / HitTestId 命中标识。
/// </summary>
public sealed class PromptContextItemTests
{
    private const string Snapshot = "【首次注入的系统提示词】\n你是 jcc 编程助手。\n【工具清单】\n  bash (system): 运行命令";

    private static string RowTextOf(Screen screen, int row, int width = 120) => RowText.Of(screen, row, 0, width).TrimEnd();

    private static (string Text, int Total, int System) Inventory() =>
        ("【工具清单】" + Environment.NewLine + "  bash (system): 运行命令", 505, 35);

    [Fact]
    public void Collapsed_RendersSummaryLineWithExpandHint()
    {
        var item = new PromptContextItem(Snapshot, Snapshot.Length, Inventory);
        using var screen = new Screen(cols: 120, rows: 3);

        Assert.Equal(1, item.MeasureHeight(120));
        item.Render(screen, new Rect(0, 0, 120, 3), Theme.Dark);

        var row = RowTextOf(screen, 0);
        Assert.Contains("📎 首次注入", row);
        Assert.Contains($"{Snapshot.Length} 字符", row);
        Assert.Contains("工具清单", row);
        Assert.Contains("点击展开", row);
    }

    [Fact]
    public void Toggle_Expanded_RendersSnapshotLinesAndCollapseHint()
    {
        var item = new PromptContextItem(Snapshot, Snapshot.Length, Inventory);
        item.Toggle();
        using var screen = new Screen(cols: 120, rows: 8);

        // 高度 = 摘要 1 + 快照 4 行 + 空行分隔 1 + 工具清单 2 行 = 8
        Assert.Equal(8, item.MeasureHeight(120));
        item.Render(screen, new Rect(0, 0, 120, 8), Theme.Dark);

        Assert.Contains("点击收起", RowTextOf(screen, 0));
        var body = string.Join("\n", Enumerable.Range(1, 4).Select(r => RowTextOf(screen, r).Trim()));
        Assert.Contains("【首次注入的系统提示词】", body);
        Assert.Contains("你是 jcc 编程助手。", body);
        Assert.Contains("bash (system): 运行命令", body);
    }

    [Fact]
    public void Toggle_Twice_BackToCollapsed()
    {
        var item = new PromptContextItem(Snapshot, Snapshot.Length, Inventory);
        item.Toggle();
        item.Toggle();
        Assert.Equal(1, item.MeasureHeight(120));
    }

    [Fact]
    public void HitTestId_IsPromptContextConstant()
    {
        var item = new PromptContextItem(Snapshot, 10, Inventory);
        Assert.Equal(PromptContextItem.HitIdValue, item.HitTestId);
        Assert.Equal("prompt-context", PromptContextItem.HitIdValue);
    }

    [Fact]
    public void WideSnapshot_WrapsToMultipleLines()
    {
        var longLine = "x" + new string('长', 200); // 400+ 列宽 — 必然折行
        var item = new PromptContextItem(longLine, longLine.Length, Inventory);
        item.Toggle();

        Assert.True(item.MeasureHeight(80) > 2);
    }
}
