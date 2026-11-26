namespace NativeTui.Tests.Repl;

/// <summary>
/// TASK069 #18 — 脱敏思考（redacted_thinking）渲染：对齐基准 AssistantRedactedThinkingMessage，
/// 默认交互视图不渲染思考行（基准 !transcriptMode &amp;&amp; !verbose → null）；transcript/verbose
/// 投影 "✻ Thinking…" 存根（dim italic 单行）。
/// </summary>
public sealed class RedactedThinkingRenderTests
{
    [Fact]
    public void Project_RedactedAssistant_EmitsThinkingStub()
    {
        var item = new AssistantMessageItem("answer", thinkingDurationMs: 5000, thinkingContent: null, messageId: 1, thinkingRedacted: true);

        var lines = TranscriptLines.Project(item).ToList();

        Assert.Contains("✻ Thinking…", lines);
        Assert.Contains(lines, line => line.EndsWith("answer", StringComparison.Ordinal));
    }

    [Fact]
    public void Project_NormalAssistant_NoStub()
    {
        var item = new AssistantMessageItem("answer", thinkingDurationMs: 5000, thinkingContent: "real cot", messageId: 1);

        var lines = TranscriptLines.Project(item).ToList();

        Assert.DoesNotContain("✻ Thinking…", lines);
        // TASK107 — CoT 行带 ∴ 前缀（详情页与正文区分）
        Assert.Contains(lines, line => line.EndsWith("real cot", StringComparison.Ordinal));
    }

    /// <summary>默认视图：脱敏思考不渲染 "∴ 思考" 摘要行（对齐基准默认 return null），高度不增。</summary>
    [Fact]
    public void Render_Redacted_SkipsThinkingSummaryLine()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new AssistantMessageItem("answer", thinkingDurationMs: 5000, thinkingContent: null, messageId: 1, thinkingRedacted: true);

        Assert.Equal(2, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal($"{Figures.MessageBullet} answer", RowText.Of(screen, 1, 0, 8));
    }

    /// <summary>正常思考仍渲染摘要行（回归守卫 — 未脱敏路径零变化）。</summary>
    [Fact]
    public void Render_Normal_RendersThinkingSummaryLine()
    {
        using var screen = new Screen(cols: 40, rows: 5);
        var item = new AssistantMessageItem("answer", thinkingDurationMs: 5000, thinkingContent: "cot", messageId: 1);

        Assert.Equal(3, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        Assert.Equal($"{Figures.MessageBullet} answer", RowText.Of(screen, 2, 0, 8));
    }
}
