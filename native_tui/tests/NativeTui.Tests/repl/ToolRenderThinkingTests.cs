namespace NativeTui.Tests.Repl;

/// <summary>
/// 思考行形态对齐测试（S2 工具折叠渲染域）— RenderThinkingSummary ∴ 前缀改造：
/// "⎿ 思考 Ns" → "∴ 思考"（时长不再显示 — 归 spinner 行 "thought for Ns" 已有，
/// SpinnerItem ThinkingState.ThoughtFor 消费）；收起态 " · ctrl+o 展开推理"、展开态 " ▾" 保留；
/// AssistantMessageItem 集成（思考摘要行 + 正文两行结构不变）。
/// </summary>
public sealed class ToolRenderThinkingTests
{
    [Fact]
    public void Collapsed_ThereforeSignPrefix_NoDuration()
    {
        // 收起态 — "∴ 思考 · ctrl+o 展开推理" 整行 dim，无时长数字（时长归 spinner 行）
        using var screen = new Screen(cols: 40, rows: 3);
        MessageRenderer.RenderThinkingSummary(screen, new Rect(0, 0, 40, 1), Theme.Dark, durationMs: 3200, expanded: false);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("∴ 思考 · ctrl+o 展开推理", RowText.Of(screen, 0, 0, 40).TrimEnd());
        Assert.Equal(new Rune('∴'), screen[0, 0].Value);
        Assert.Equal(dim, screen[0, 0].StyleId);
        Assert.DoesNotContain("3s", RowText.Of(screen, 0, 0, 40));
        Assert.DoesNotContain("⎿", RowText.Of(screen, 0, 0, 40));
    }

    [Fact]
    public void Expanded_CaretSuffix_KeepsPrefix()
    {
        // 展开态 — "∴ 思考 ▾"（前缀保持 ∴）
        using var screen = new Screen(cols: 40, rows: 3);
        MessageRenderer.RenderThinkingSummary(screen, new Rect(0, 0, 40, 1), Theme.Dark, durationMs: 500, expanded: true);

        Assert.Equal("∴ 思考 ▾", RowText.Of(screen, 0, 0, 40).TrimEnd());
    }

    [Fact]
    public void AssistantMessageItem_ThinkingRow_RendersThereforeForm()
    {
        // AssistantMessageItem 集成 — 思考摘要行 ∴ 形态 + 正文结构不变（margin + 思考行 + 正文）
        using var screen = new Screen(cols: 40, rows: 6);
        var item = new AssistantMessageItem("正文", thinkingDurationMs: 3200, thinkingContent: "CoT", messageId: 1);

        Assert.Equal(3, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 2, 40, 3), Theme.Dark);

        Assert.Equal("∴ 思考 · ctrl+o 展开推理", screen.RowText(3).TrimEnd());
        Assert.Equal("● 正文", screen.RowText(4).TrimEnd());
    }

    [Fact]
    public void ExpandedCoT_ContentRowsKeepIndent()
    {
        // 展开 CoT — 内容行 ⎿+2 缩进 dim 逐行（既有形态不动），仅摘要行前缀改 ∴
        using var screen = new Screen(cols: 60, rows: 8);
        var item = new AssistantMessageItem("正文", thinkingDurationMs: 500, thinkingContent: "第一行推理\n第二行推理", messageId: 1);

        item.ToggleThinking();
        Assert.Equal(2 + 1 + 2, item.MeasureHeight(60));
        item.Render(screen, new Rect(0, 2, 60, 5), Theme.Dark);

        Assert.Equal("∴ 思考 ▾", screen.RowText(3).TrimEnd());
        Assert.Contains("第一行推理", screen.RowText(4));
        Assert.Contains("第二行推理", screen.RowText(5));
    }
}
