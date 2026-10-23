namespace NativeTui.Tests.Repl.Attachments;

/// <summary>
/// 附件占位项测试 — 渲染形态对齐基准 UserImageMessage.tsx:20-58：margin 1 + 标签行
///（"  ⎿  " dim 前缀 + 默认正文 [Image #N] — MessageResponse 连接样式）；
/// imageId 无效退化 [Image]（基准 26 行）；transcript 文本匹配。
/// </summary>
public sealed class ImageAttachmentItemTests
{
    [Fact]
    public void MeasureHeight_IsTwoRowsRegardlessOfWidth()
    {
        Assert.Equal(2, new ImageAttachmentItem(imageId: 3).MeasureHeight(width: 60));
        Assert.Equal(2, new ImageAttachmentItem(imageId: 0).MeasureHeight(width: 10));
    }

    [Fact]
    public void Render_MarginRowBlankThenIndentedLabel()
    {
        using var screen = new Screen(cols: 40, rows: 2);
        var item = new ImageAttachmentItem(imageId: 3, filename: "shot.png");
        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);

        // margin 行留空（空白 cell 无样式 — 对齐 DiffResultItemTests 先例）
        Assert.Equal(new Rune(' '), screen[0, 0].Value);
        Assert.Equal(0, screen[0, 0].StyleId);
        // 标签行：⎿ 前缀 dim + 正文默认样式（MessageResponse 形态 — 基准 UserImageMessage 无 margin 包裹）
        Assert.Equal("  ⎿  [Image #3]", RowText.Of(screen, 1, 0, 40).TrimEnd());
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(new Rune('⎿'), screen[1, 2].Value);
        Assert.Equal(dim, screen[1, 2].StyleId);
        Assert.Equal(new Rune('['), screen[1, Figures.ToolResultIndent.Length].Value);
        Assert.Equal(0, screen[1, Figures.ToolResultIndent.Length].StyleId);
    }

    [Fact]
    public void Render_ZeroImageId_FallsBackToBareLabel()
    {
        // 基准 UserImageMessage.tsx:26 — imageId 无效 [Image]
        using var screen = new Screen(cols: 40, rows: 2);
        var item = new ImageAttachmentItem(imageId: 0);
        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);
        Assert.Equal("  ⎿  [Image]", RowText.Of(screen, 1, 0, 40).TrimEnd());
    }

    [Fact]
    public void Label_MirrorsImageId()
    {
        Assert.Equal("[Image #7]", new ImageAttachmentItem(imageId: 7).Label);
        Assert.Equal("[Image]", new ImageAttachmentItem(imageId: -1).Label);
    }

    [Fact]
    public void Filename_ExposedForAssertions()
    {
        Assert.Equal("shot.png", new ImageAttachmentItem(imageId: 3, filename: "shot.png").Filename);
        Assert.Null(new ImageAttachmentItem(imageId: 3).Filename);
    }

    [Fact]
    public void ContainsText_MatchesLabelAndFilename()
    {
        var item = new ImageAttachmentItem(imageId: 3, filename: "shot.png");
        Assert.True(item.ContainsText("image #3"));
        Assert.True(item.ContainsText("shot"));
        Assert.True(item.ContainsText("PNG"));
        Assert.False(item.ContainsText("jpeg"));
    }
}
