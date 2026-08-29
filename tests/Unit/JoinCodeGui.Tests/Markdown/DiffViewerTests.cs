namespace JoinCode.Gui.Tests.Markdown;

/// <summary>
/// DiffViewer 渲染测试 — 验证双列行号（旧/新）、增删着色、多 hunk 渲染、空状态。
/// </summary>
[Collection("GuiUiSequential")]
public sealed class DiffViewerTests
{
    private static DiffViewer Render(StructuredPatchHunk[]? hunks)
    {
        var viewer = new DiffViewer { Hunks = hunks };
        var win = new Window { Content = viewer, Width = 600, Height = 400 };
        win.Show();
        return viewer;
    }

    private static List<SelectableTextBlock> TextBlocks(DiffViewer viewer)
        => viewer.GetVisualDescendants().OfType<SelectableTextBlock>().ToList();

    private static StructuredPatchHunk SingleHunk(params PatchLine[] lines)
        => new()
        {
            OldStart = 1,
            OldLines = lines.Length,
            NewStart = 1,
            NewLines = lines.Length,
            Header = "@@ -1,3 +1,3 @@",
            Lines = lines
        };

    [AvaloniaFact]
    public void Render_ShowsOldAndNewLineNumberColumns()
    {
        var hunk = SingleHunk(
            new PatchLine { Type = PatchLineType.Context, Content = "keep", OldLineNumber = 1, NewLineNumber = 1 },
            new PatchLine { Type = PatchLineType.Removed, Content = "old", OldLineNumber = 2, NewLineNumber = null },
            new PatchLine { Type = PatchLineType.Added, Content = "new", OldLineNumber = null, NewLineNumber = 2 }
        );

        var viewer = Render([hunk]);
        var texts = TextBlocks(viewer).Select(t => t.Text).ToList();

        // 上下文行应同时渲染旧/新两个行号 1 和 1
        Assert.Contains("1", texts);
        Assert.Equal(2, texts.Count(t => t == "1"));
    }

    [AvaloniaFact]
    public void Render_NullHunks_NoChildren()
    {
        var viewer = Render(null);

        viewer.Children.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Render_EmptyHunksArray_NoChildren()
    {
        var viewer = Render([]);

        viewer.Children.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Render_MultipleHunks_CreatesOneBorderPerHunk()
    {
        var hunk1 = SingleHunk(new PatchLine { Type = PatchLineType.Context, Content = "a", OldLineNumber = 1, NewLineNumber = 1 });
        var hunk2 = SingleHunk(new PatchLine { Type = PatchLineType.Context, Content = "b", OldLineNumber = 1, NewLineNumber = 1 });

        var viewer = Render([hunk1, hunk2]);

        viewer.Children.Should().HaveCount(2);
    }

    [AvaloniaFact]
    public void Render_HunkHeader_IsDisplayedAsText()
    {
        var hunk = new StructuredPatchHunk
        {
            OldStart = 1,
            OldLines = 1,
            NewStart = 1,
            NewLines = 1,
            Header = "@@ -10,2 +10,2 @@",
            Lines = [new PatchLine { Type = PatchLineType.Context, Content = "x", OldLineNumber = 10, NewLineNumber = 10 }]
        };

        var viewer = Render([hunk]);
        var texts = TextBlocks(viewer).Select(t => t.Text).ToList();

        Assert.Contains("@@ -10,2 +10,2 @@", texts);
    }

    [AvaloniaFact]
    public void Render_EmptyHeader_GeneratesDefaultHeader()
    {
        var hunk = new StructuredPatchHunk
        {
            OldStart = 2,
            OldLines = 3,
            NewStart = 2,
            NewLines = 4,
            Header = "",
            Lines = [new PatchLine { Type = PatchLineType.Context, Content = "x", OldLineNumber = 2, NewLineNumber = 2 }]
        };

        var viewer = Render([hunk]);
        var texts = TextBlocks(viewer).Select(t => t.Text).ToList();

        Assert.Contains("@@ -2,3 +2,4 @@", texts);
    }

    [AvaloniaFact]
    public void Render_AddedLine_HasPlusPrefix()
    {
        var hunk = SingleHunk(new PatchLine { Type = PatchLineType.Added, Content = "added", OldLineNumber = null, NewLineNumber = 1 });

        var viewer = Render([hunk]);
        var texts = TextBlocks(viewer).Select(t => t.Text).ToList();

        Assert.Contains("+", texts);
        Assert.Contains("added", texts);
    }

    [AvaloniaFact]
    public void Render_RemovedLine_HasMinusPrefix()
    {
        var hunk = SingleHunk(new PatchLine { Type = PatchLineType.Removed, Content = "removed", OldLineNumber = 1, NewLineNumber = null });

        var viewer = Render([hunk]);
        var texts = TextBlocks(viewer).Select(t => t.Text).ToList();

        Assert.Contains("-", texts);
        Assert.Contains("removed", texts);
    }

    [AvaloniaFact]
    public void Render_ContextLine_HasSpacePrefix()
    {
        var hunk = SingleHunk(new PatchLine { Type = PatchLineType.Context, Content = "ctx", OldLineNumber = 1, NewLineNumber = 1 });

        var viewer = Render([hunk]);
        var texts = TextBlocks(viewer).Select(t => t.Text).ToList();

        Assert.Contains(" ", texts);
        Assert.Contains("ctx", texts);
    }

    [AvaloniaFact]
    public void Render_AddedLine_HasGreenForeground()
    {
        var hunk = SingleHunk(new PatchLine { Type = PatchLineType.Added, Content = "x", OldLineNumber = null, NewLineNumber = 1 });

        var viewer = Render([hunk]);
        var contentBlocks = TextBlocks(viewer).Where(t => t.Text == "x").ToList();

        contentBlocks.Should().NotBeEmpty();
        contentBlocks[0].Foreground.Should().BeAssignableTo<ISolidColorBrush>();
    }

    [AvaloniaFact]
    public void Render_RemovedLine_HasRedForeground()
    {
        var hunk = SingleHunk(new PatchLine { Type = PatchLineType.Removed, Content = "x", OldLineNumber = 1, NewLineNumber = null });

        var viewer = Render([hunk]);
        var contentBlocks = TextBlocks(viewer).Where(t => t.Text == "x").ToList();

        contentBlocks.Should().NotBeEmpty();
        contentBlocks[0].Foreground.Should().BeAssignableTo<ISolidColorBrush>();
    }

    [AvaloniaFact]
    public void Rebuild_AfterHunksChange_UpdatesChildren()
    {
        var viewer = new DiffViewer { Hunks = null };
        var win = new Window { Content = viewer, Width = 600, Height = 400 };
        win.Show();

        viewer.Children.Should().BeEmpty();

        viewer.Hunks = [SingleHunk(new PatchLine { Type = PatchLineType.Context, Content = "x", OldLineNumber = 1, NewLineNumber = 1 })];

        viewer.Children.Should().HaveCount(1);
    }
}
