namespace NativeTui.Tests.Repl;

/// <summary>
/// diff 统计头测试（TASK069-M3 — 对齐基准 components/FileEditToolUpdatedMessage.tsx L24-55）：
/// `Added N lines, removed M lines`；单侧变更省略另一侧；单复数按 &gt;1 判；数字 bold、文案默认样式；
/// 无变更不出统计行（回退引擎摘要）；无 patch 时 DisplaySummary 即摘要。
/// </summary>
public sealed class DiffStatsHeaderTests
{
    private static DiffLine Line(DiffLineKind kind, string content) => new() { Kind = kind, Content = content };

    private static DiffHunk Hunk(params DiffLine[] lines) => new() { Header = "@@ -1,2 +1,2 @@", Lines = lines };

    [Fact]
    public void From_CountsBothSidesAcrossHunks()
    {
        var stats = DiffStats.From(
        [
            Hunk(Line(DiffLineKind.Added, "a"), Line(DiffLineKind.Removed, "b"), Line(DiffLineKind.Context, "c")),
            Hunk(Line(DiffLineKind.Added, "d")),
        ]);

        Assert.Equal(2, stats.Added);
        Assert.Equal(1, stats.Removed);
        Assert.False(stats.IsEmpty);
    }

    [Fact]
    public void Text_BothSides()
    {
        Assert.Equal("Added 2 lines, removed 1 line", new DiffStats(2, 1).ToText());
    }

    [Fact]
    public void Text_AdditionsOnly()
    {
        Assert.Equal("Added 1 line", new DiffStats(1, 0).ToText());
    }

    [Fact]
    public void Text_RemovalsOnly()
    {
        // 基准 numAdditions === 0 时首字母大写（"R"emoved）
        Assert.Equal("Removed 3 lines", new DiffStats(0, 3).ToText());
    }

    [Fact]
    public void Text_NoChange_IsEmpty()
    {
        var stats = new DiffStats(0, 0);

        Assert.True(stats.IsEmpty);
        Assert.Equal(string.Empty, stats.ToText());
        Assert.Empty(stats.Segments());
    }

    [Fact]
    public void Render_NumbersBoldOtherSegmentsDefaultStyle()
    {
        var patch = new[]
        {
            Hunk(
                Line(DiffLineKind.Added, "new one"),
                Line(DiffLineKind.Added, "new two"),
                Line(DiffLineKind.Removed, "old"),
                Line(DiffLineKind.Context, "same")),
        };
        using var screen = new Screen(cols: 60, rows: 8);
        var item = new ToolResultItem("The file a.cs has been updated", patch, null, hiddenLines: 0);

        Assert.Equal("Added 2 lines, removed 1 line", item.DisplaySummary);
        // margin 1 + 统计行 1 + hunk 预览 5（header + 4 行）
        Assert.Equal(7, item.MeasureHeight(60));
        item.Render(screen, new Rect(0, 0, 60, 8), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        Assert.Equal(dim, screen[1, 2].StyleId);
        Assert.Equal("  ⎿  Added 2 lines, removed 1 line", RowText.Of(screen, 1, 0, 34));
        Assert.Equal(0, screen[1, 5].StyleId);   // "Added " 默认样式
        Assert.Equal(bold, screen[1, 11].StyleId); // 首个数字 bold
        Assert.Equal(bold, screen[1, 28].StyleId); // 第二个数字 bold
    }

    [Fact]
    public void Render_PatchWithoutChanges_KeepsEngineSummary()
    {
        // 空 patch（两侧皆 0）不占统计行，首行回退引擎摘要
        var patch = new[] { Hunk(Line(DiffLineKind.Context, "same")) };
        using var screen = new Screen(cols: 40, rows: 5);
        var item = new ToolResultItem("No changes", patch, null, hiddenLines: 0);

        Assert.True(item.Stats is { IsEmpty: true });
        Assert.Equal("No changes", item.DisplaySummary);
        item.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        Assert.Equal("  ⎿  No changes", RowText.Of(screen, 1, 0, 15));
    }

    [Fact]
    public void NoPatch_DisplaySummaryIsEngineSummary()
    {
        var item = new ToolResultItem("41 lines");

        Assert.Null(item.Stats);
        Assert.Equal("41 lines", item.DisplaySummary);
    }

    [Fact]
    public void Transcript_PatchItem_ProjectsStatsHeader()
    {
        var patch = new[] { Hunk(Line(DiffLineKind.Added, "new")) };
        var item = new ToolResultItem("The file a.cs has been updated", patch, null, hiddenLines: 0);

        var lines = TranscriptLines.Project(item).ToList();

        Assert.Equal("  ⎿  Added 1 line", lines[0]);
    }
}
