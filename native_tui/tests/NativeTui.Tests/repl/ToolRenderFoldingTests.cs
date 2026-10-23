namespace NativeTui.Tests.Repl;

/// <summary>
/// 工具结果折叠渲染测试（S2 工具折叠渲染域）— ToolResultItem 统一四段契约：
/// Bash 3 行折叠（terminal.ts MAX_LINES_TO_SHOW=3：前 3 行 + "… +N lines (ctrl+o)" 提示）；
/// 差 1 行特例（remaining==1 直接显示剩余全部不出提示 — terminal.ts L44-54）；
/// 无输出 "(No output)" dim（BashToolResultMessage.tsx L156）；Write 10 行预览
///（FileWriteTool/UI.tsx MAX_LINES_TO_RENDER=10）；展开态全量（ctrl+o 消费完整数据）；
/// DiffResultItem 薄委托回归（渲染/断言面与既有行为一致）。
/// </summary>
public sealed class ToolRenderFoldingTests
{
    /// <summary>构造五行输出（"line1".."line5"）。</summary>
    private static string FiveLines() => string.Join("\n", Enumerable.Range(1, 5).Select(i => $"line{i}"));

    [Fact]
    public void LegacyText_SingleLine_RendersAsBefore()
    {
        // 旧单参构造 — 纯摘要形态（排队反馈等场景兼容），渲染与高度不变
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new ToolResultItem("42 lines");

        Assert.Equal(2, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal("  ⎿  42 lines", RowText.Of(screen, 1, 0, 13));
        Assert.Equal(new Rune(' '), screen[0, 0].Value);
    }

    [Fact]
    public void BashOutput_BeyondThreeLines_ShowsPreviewPlusHint()
    {
        // 5 行输出 hidden=2 → 前 3 行 + "… +2 lines (ctrl+o)" dim 提示（terminal.ts L103-113）
        using var screen = new Screen(cols: 40, rows: 8);
        var item = new ToolResultItem("Done", null, FiveLines(), hiddenLines: 2);

        Assert.Equal(6, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 8), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("  ⎿  Done", RowText.Of(screen, 1, 0, 9));
        Assert.Equal("     line1", RowText.Of(screen, 2, 0, 10));
        Assert.Equal("     line2", RowText.Of(screen, 3, 0, 10));
        Assert.Equal("     line3", RowText.Of(screen, 4, 0, 10));
        Assert.Equal("     … +2 lines (ctrl+o)", RowText.Of(screen, 5, 0, 24));
        Assert.Equal(dim, screen[5, 5].StyleId);
        Assert.Equal(new Rune(' '), screen[6, 0].Value);
    }

    [Fact]
    public void BashOutput_HiddenExactlyOne_ShowsRemainingWithoutHint()
    {
        // 差 1 行特例 — 5 行 hidden=1 → 显示 4 行（3+1），不出 "+1 line" 提示（terminal.ts L44-54）
        using var screen = new Screen(cols: 40, rows: 8);
        var item = new ToolResultItem("Done", null, FiveLines(), hiddenLines: 1);

        Assert.Equal(6, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 8), Theme.Dark);

        Assert.Equal("     line4", RowText.Of(screen, 5, 0, 10));
        Assert.Equal(new Rune(' '), screen[6, 0].Value);
    }

    [Fact]
    public void BashOutput_ZeroHidden_ShowsAllLines()
    {
        // HiddenLines=0 — 无折叠全量显示（短输出场景）
        using var screen = new Screen(cols: 40, rows: 6);
        var item = new ToolResultItem("Done", null, "line1\nline2", hiddenLines: 0);

        Assert.Equal(4, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark);

        Assert.Equal("     line1", RowText.Of(screen, 2, 0, 10));
        Assert.Equal("     line2", RowText.Of(screen, 3, 0, 10));
    }

    [Fact]
    public void BlankOutput_RendersNoOutputPlaceholder_Dim()
    {
        // 无输出 — "(No output)" dim 占位（BashToolResultMessage.tsx L156 stdout/stderr 皆空）
        using var screen = new Screen(cols: 40, rows: 5);
        var item = new ToolResultItem("", null, "  \n  ", hiddenLines: 0);

        Assert.Equal(2, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("  ⎿  (No output)", RowText.Of(screen, 1, 0, 16));
        Assert.Equal(dim, screen[1, 5].StyleId);
    }

    [Fact]
    public void EmptySummary_FirstOutputLine_InlinesWithPrefix()
    {
        // summary 空 — 首输出行直接与 ⎿ 前缀同行（基准 Bash：stdout 即 content 无独立摘要行）
        using var screen = new Screen(cols: 40, rows: 5);
        var item = new ToolResultItem("", null, "stdout 第一行\nstdout 第二行", hiddenLines: 0);

        Assert.Equal(3, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        Assert.Equal("  ⎿  stdout 第一行", RowText.Of(screen, 1, 0, 20).TrimEnd());
        Assert.Equal("     stdout 第二行", RowText.Of(screen, 2, 0, 20).TrimEnd());
    }

    [Fact]
    public void Expanded_FullOutputWithoutHint()
    {
        // 展开态（ctrl+o）— RawOutput 全量渲染不出提示；SetExpanded 切换 + 高度增长
        using var screen = new Screen(cols: 40, rows: 9);
        var item = new ToolResultItem("Done", null, FiveLines(), hiddenLines: 2);

        item.SetExpanded(true);
        Assert.True(item.IsExpanded);
        Assert.Equal(7, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 9), Theme.Dark);

        Assert.Equal("     line4", RowText.Of(screen, 5, 0, 10));
        Assert.Equal("     line5", RowText.Of(screen, 6, 0, 10));
        Assert.Equal(new Rune(' '), screen[7, 0].Value);

        item.SetExpanded(false);
        Assert.False(item.IsExpanded);
        Assert.Equal(6, item.MeasureHeight(40));
    }

    [Fact]
    public void Expanded_HoldsFullRawOutput_ForCtrlO()
    {
        // item 恒持有完整 RawOutput（折叠只影响显示不影响存储 — ctrl+o 展开数据源）
        var item = new ToolResultItem("Done", null, FiveLines(), hiddenLines: 2);

        Assert.Equal(FiveLines(), item.RawOutput);
        Assert.Equal(2, item.HiddenLines);
        Assert.Equal(ToolFoldingPolicy.MaxToolOutputLines, item.PreviewLimit);
    }

    [Fact]
    public void WriteOutput_PreviewLimitTen_ShowsTenLinesPlusHint()
    {
        // Write 10 行折叠 — previewLimit=10：15 行输出 hidden=5 → 10 行 + "… +5 lines" 提示
        var content = string.Join("\n", Enumerable.Range(1, 15).Select(i => $"code line {i}"));
        using var screen = new Screen(cols: 60, rows: 16);
        var item = new ToolResultItem("写入 15 行到 src/gen.cs", null, content, hiddenLines: 5,
            previewLimit: ToolFoldingPolicy.MaxWritePreviewLines);

        Assert.Equal(13, item.MeasureHeight(60));
        item.Render(screen, new Rect(0, 0, 60, 16), Theme.Dark);

        Assert.Equal("  ⎿  写入 15 行到 src/gen.cs", RowText.Of(screen, 1, 0, 40).TrimEnd());
        Assert.Equal("     code line 10", RowText.Of(screen, 11, 0, 17));
        Assert.Equal("     … +5 lines (ctrl+o)", RowText.Of(screen, 12, 0, 24));
        Assert.Equal(new Rune(' '), screen[13, 0].Value);
    }

    [Fact]
    public void PatchAndRawOutput_RenderInSequence()
    {
        // patch + rawOutput 组合 — summary → hunk 预览 → 输出折叠块依序渲染
        //（rawOutput 5 行 hidden=1 差 1 行特例 → 显示 4 行不出提示）
        var patch = new[]
        {
            new DiffHunk { Header = "@@ -1 +1 @@", Lines = [new DiffLine { Kind = DiffLineKind.Added, Content = "new" }] },
        };
        using var screen = new Screen(cols: 40, rows: 8);
        var item = new ToolResultItem("Updated a.cs", patch, "out1\nout2\nout3\nout4\nout5", hiddenLines: 1);

        Assert.Equal(1 + 1 + 2 + 4, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 8), Theme.Dark);

        Assert.Equal("  ⎿  Updated a.cs", RowText.Of(screen, 1, 0, 17));
        Assert.Equal("    @@ -1 +1 @@", RowText.Of(screen, 2, 0, 15));
        Assert.Equal("     1 +new", RowText.Of(screen, 3, 0, 11));
        Assert.Equal("     out1", RowText.Of(screen, 4, 0, 9));
        Assert.Equal("     out4", RowText.Of(screen, 7, 0, 9));
    }

    [Fact]
    public void DiffResultItem_DelegatesToCore_SameSurfaceAsBefore()
    {
        // DiffResultItem 薄委托回归 — Summary/Patch 断言面 + 渲染输出与既有行为一致
        var hunks = new[]
        {
            new DiffHunk
            {
                Header = "@@ -1,3 +1,4 @@",
                Lines = [new DiffLine { Kind = DiffLineKind.Added, Content = "new line" }],
            },
        };
        using var screen = new Screen(cols: 40, rows: 5);
        var item = new DiffResultItem("Edited a.cs", hunks);

        Assert.Equal("Edited a.cs", item.Summary);
        Assert.Same(hunks, item.Patch);
        Assert.Equal(4, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        Assert.Equal("  ⎿  Edited a.cs", RowText.Of(screen, 1, 0, 16));
        Assert.Equal("    @@ -1,3 +1,4 @@", RowText.Of(screen, 2, 0, 19));
        Assert.Equal("     1 +new line", RowText.Of(screen, 3, 0, 16));
    }

    [Fact]
    public void TrailingNewline_TreatedAsTerminator()
    {
        // 尾部换行 = 行终结符不计新行（terminal.ts trimEnd 口径）— "a\nb\n" → 2 行全量
        using var screen = new Screen(cols: 40, rows: 5);
        var item = new ToolResultItem("Done", null, "a\nb\n", hiddenLines: 0);

        Assert.Equal(4, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        Assert.Equal("     a", RowText.Of(screen, 2, 0, 6));
        Assert.Equal("     b", RowText.Of(screen, 3, 0, 6));
    }
}
