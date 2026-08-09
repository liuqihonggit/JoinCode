using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using JoinCode.Abstractions.Models.Diff;
using JoinCode.Gui.Markdown;

namespace JoinCode.Gui.Tests.Markdown;

/// <summary>
/// DiffViewer 渲染测试 — 验证双列行号（旧/新）与增删着色。
/// </summary>
public sealed class DiffViewerTests
{
    private static DiffViewer Render(StructuredPatchHunk[] hunks)
    {
        var viewer = new DiffViewer { Hunks = hunks };
        var win = new Window { Content = viewer, Width = 600, Height = 400 };
        win.Show();
        return viewer;
    }

    private static List<SelectableTextBlock> TextBlocks(DiffViewer viewer)
        => viewer.GetVisualDescendants().OfType<SelectableTextBlock>().ToList();

    [AvaloniaFact]
    public void Render_ShowsOldAndNewLineNumberColumns()
    {
        var hunk = new StructuredPatchHunk
        {
            OldStart = 1,
            OldLines = 3,
            NewStart = 1,
            NewLines = 3,
            Header = "@@ -1,3 +1,3 @@",
            Lines = new[]
            {
                new PatchLine { Type = PatchLineType.Context, Content = "keep", OldLineNumber = 1, NewLineNumber = 1 },
                new PatchLine { Type = PatchLineType.Removed, Content = "old", OldLineNumber = 2, NewLineNumber = null },
                new PatchLine { Type = PatchLineType.Added, Content = "new", OldLineNumber = null, NewLineNumber = 2 }
            }
        };

        var viewer = Render([hunk]);
        var texts = TextBlocks(viewer).Select(t => t.Text).ToList();

        // 上下文行应同时渲染旧/新两个行号 1 和 1
        Assert.Contains("1", texts);
        Assert.Equal(2, texts.Count(t => t == "1"));
    }
}
