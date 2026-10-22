namespace NativeTui.Markdown.Tests;

/// <summary>
/// 流式 Markdown 增量投影器测试（方案 B — 块级增量，对齐基准 Markdown.tsx 块边界切分机制）：
/// 块边界切分（单增长块全尾 / 多块首块稳定）；未闭合围栏 TailOpenFence；
/// memoize（相同长度缓存 + 切分点推进才追加投影 + 逐 delta 序列与最终直接投影等价）；
/// 无语法快路径全尾；稳定块行内样式投影（与 MarkdigProjector 同一分派）。
/// </summary>
public sealed class StreamingMarkdownProjectorTests
{
    /// <summary>取稳定块中唯一段落。</summary>
    private static MarkdownParagraph SingleStableParagraph(StreamingMarkdownProjection projection)
    {
        var block = Assert.Single(projection.StableBlocks);
        return Assert.IsType<MarkdownParagraph>(block);
    }

    [Fact]
    public void Project_EmptyText_AllTailNoBlocks()
    {
        var projection = new StreamingMarkdownProjector().Project(string.Empty);

        Assert.Empty(projection.StableBlocks);
        Assert.Equal(string.Empty, projection.Tail);
        Assert.False(projection.TailOpenFence);
    }

    [Fact]
    public void Project_NoSyntax_PlainTextFastPath()
    {
        // 无语法快路径 — 纯文本全尾零解析（与落定 ProjectOptional 同判定，视觉一致）
        var projection = new StreamingMarkdownProjector().Project("just plain text");

        Assert.Empty(projection.StableBlocks);
        Assert.Equal("just plain text", projection.Tail);
        Assert.False(projection.TailOpenFence);
    }

    [Fact]
    public void Project_SingleGrowingBlock_AllTail()
    {
        // 单增长块（含语法字符）— 无前序块边界，全尾纯文本
        var projection = new StreamingMarkdownProjector().Project("para with *em*");

        Assert.Empty(projection.StableBlocks);
        Assert.Equal("para with *em*", projection.Tail);
    }

    [Fact]
    public void Project_TwoParagraphs_FirstStableSecondTail()
    {
        var projection = new StreamingMarkdownProjector().Project("p1\n\np2");

        var paragraph = SingleStableParagraph(projection);
        var segment = Assert.Single(paragraph.Segments);
        Assert.Equal("p1", segment.Text);
        Assert.Equal("p2", projection.Tail);
        Assert.False(projection.TailOpenFence);
    }

    [Fact]
    public void Project_HeadingStable_ParagraphTail()
    {
        var projection = new StreamingMarkdownProjector().Project("# T\n\np");

        var heading = Assert.IsType<MarkdownHeading>(Assert.Single(projection.StableBlocks));
        Assert.Equal(1, heading.Level);
        Assert.Equal("p", projection.Tail);
    }

    [Fact]
    public void Project_Memoize_SameLengthReturnsCached()
    {
        var projector = new StreamingMarkdownProjector();
        var first = projector.Project("p1\n\np2");

        var second = projector.Project("p1\n\np2");

        Assert.Same(first, second);
        Assert.Same(first.StableBlocks, second.StableBlocks);
    }

    [Fact]
    public void Project_AppendCrossesBoundary_StableBlocksGrow()
    {
        var projector = new StreamingMarkdownProjector();
        _ = projector.Project("p1\n\np2");

        var projection = projector.Project("p1\n\np2\n\np3");

        Assert.Equal(2, projection.StableBlocks.Count);
        Assert.Equal("p3", projection.Tail);
    }

    [Fact]
    public void Project_SuccessiveDeltas_EquivalentToFinalProjection()
    {
        // memoize 增量正确性 — 逐 delta 投影的稳定前缀与最终文本一次性投影等价（块内容级）
        var incremental = new StreamingMarkdownProjector();
        var accumulated = string.Empty;
        foreach (var delta in new[] { "a", "\n\n", "b", "more", "\n\n", "c" })
        {
            accumulated += delta;
            incremental.Project(accumulated);
        }

        var incrementalFinal = incremental.Project(accumulated);
        var freshFinal = new StreamingMarkdownProjector().Project(accumulated);

        Assert.Equal(
            freshFinal.StableBlocks.Select(static block => block is MarkdownParagraph paragraph
                ? string.Join("|", paragraph.Segments.Select(static segment => segment.Text))
                : block.ToString()),
            incrementalFinal.StableBlocks.Select(static block => block is MarkdownParagraph paragraph
                ? string.Join("|", paragraph.Segments.Select(static segment => segment.Text))
                : block.ToString()));
        Assert.Equal(freshFinal.Tail, incrementalFinal.Tail);
    }

    [Fact]
    public void Project_UnclosedFence_TailOpenFenceTrue()
    {
        var projection = new StreamingMarkdownProjector().Project("intro\n\n```cs\ncode");

        var paragraph = SingleStableParagraph(projection);
        var segment = Assert.Single(paragraph.Segments);
        Assert.Equal("intro", segment.Text);
        Assert.Equal("```cs\ncode", projection.Tail);
        Assert.True(projection.TailOpenFence);
    }

    [Fact]
    public void Project_ClosedFenceTail_NoOpenFence()
    {
        var projection = new StreamingMarkdownProjector().Project("a\n\n```\nc\n```");

        SingleStableParagraph(projection);
        Assert.Equal("```\nc\n```", projection.Tail);
        Assert.False(projection.TailOpenFence);
    }

    [Fact]
    public void Project_StableBlockInlineStyles_Projected()
    {
        // 稳定块行内样式 — 与 MarkdigProjector 同一分派（bold flags 投影）
        var projection = new StreamingMarkdownProjector().Project("**b**\n\np2");

        var paragraph = SingleStableParagraph(projection);
        var segment = Assert.Single(paragraph.Segments);
        Assert.Equal(("b", MarkdownStyle.Bold), (segment.Text, segment.Style));
    }

    [Fact]
    public void Project_AppendWithinTail_StableBlocksUnchanged()
    {
        // 尾内追加（未跨块边界）— 稳定块引用不变（memoize：旧块永不重投影）
        var projector = new StreamingMarkdownProjector();
        var first = projector.Project("p1\n\np2");

        var second = projector.Project("p1\n\np2 appended");

        Assert.Same(first.StableBlocks, second.StableBlocks);
        Assert.Equal("p2 appended", second.Tail);
    }
}
