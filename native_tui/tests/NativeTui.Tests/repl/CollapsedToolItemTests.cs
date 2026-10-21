namespace NativeTui.Tests.Repl;

/// <summary>
/// 工具调用聚合项测试（M8-4）— 连续同名可聚合调用（Read/Grep/Glob）的单行摘要投影：
/// 单次（Count=1）渲染与 ToolCallItem 像素一致（● text 色 + bold 工具名 + 默认 (参数) — 单次视觉不变）；
/// 聚合态（Count≥2）摘要行 "● 读取 N 个文件"（● bullet + 动词 bold + 数量段默认样式）+
/// 最新操作提示行 "  ⎿  {最新参数}" 整行 dim（对齐基准 CollapsedReadSearchContent 组下方 latest 行，中文文案）。
/// </summary>
public sealed class CollapsedToolItemTests
{
    [Fact]
    public void Render_SingleCall_MatchesToolCallForm()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        var item = new CollapsedToolItem("Read", ["src/a.cs"]);

        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        // row0 margin 留空；row1 与 ToolCallItem 像素一致：● text 色 + bold 工具名 + 默认 (参数)
        var bullet = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text });
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        Assert.Equal(new Rune(' '), screen[0, 0].Value);
        Assert.Equal(new Rune('●'), screen[1, 0].Value);
        Assert.Equal(bullet, screen[1, 0].StyleId);
        Assert.Equal(bold, screen[1, 2].StyleId);
        Assert.Equal(0, screen[1, 6].StyleId);
        Assert.Equal("● Read(src/a.cs)", RowText.Of(screen, 1, 0, 16));
    }

    [Fact]
    public void Render_TwoCalls_AggregateSummaryAndLastHint()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new CollapsedToolItem("Read", ["src/a.cs", "src/b.cs"]);

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        // row1 摘要 = ● text 色 + bold "读取" + 默认 " 2 个文件"（结构对齐 RenderToolCall 的 bold 段 + 默认段；
        // 汉字占 2 cell（SpacerTail 跳过）— 15 cell 覆盖全行）
        var bullet = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text });
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        Assert.Equal("● 读取 2 个文件", RowText.Of(screen, 1, 0, 15));
        Assert.Equal(bullet, screen[1, 0].StyleId);
        Assert.Equal(bold, screen[1, 2].StyleId);
        Assert.Equal(bold, screen[1, 3].StyleId);
        Assert.Equal(bold, screen[1, 4].StyleId);
        Assert.Equal(0, screen[1, 7].StyleId);

        // row2 最新提示 = "  ⎿  src/b.cs" 整行 dim（前缀与正文同 dim 样式）
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("  ⎿  src/b.cs", RowText.Of(screen, 2, 0, 13));
        Assert.Equal(dim, screen[2, 2].StyleId);
        Assert.Equal(dim, screen[2, 5].StyleId);
    }

    [Fact]
    public void MeasureHeight_SingleCallTwoRows_AggregatedThreeRows()
    {
        Assert.Equal(2, new CollapsedToolItem("Read", ["a.cs"]).MeasureHeight(width: 80));
        Assert.Equal(3, new CollapsedToolItem("Read", ["a.cs", "b.cs"]).MeasureHeight(width: 80));
    }

    [Fact]
    public void RecordCall_IncrementsCountAndUpdatesLastArgs()
    {
        var item = new CollapsedToolItem("Read", ["a.cs"]);
        Assert.Equal(1, item.Count);
        Assert.Equal("a.cs", item.LastArgs);

        item.RecordCall("b.cs");
        item.RecordCall("c.cs");

        Assert.Equal(3, item.Count);
        Assert.Equal("c.cs", item.LastArgs);
    }

    [Fact]
    public void SummaryText_PerToolChineseVerbs()
    {
        Assert.Equal("读取 2 个文件", new CollapsedToolItem("Read", ["a.cs", "b.cs"]).SummaryText);
        Assert.Equal("搜索 3 次", new CollapsedToolItem("Grep", ["a", "b", "c"]).SummaryText);
        Assert.Equal("查找 2 个模式", new CollapsedToolItem("Glob", ["*.cs", "*.ts"]).SummaryText);
        Assert.Equal("Bash × 2", new CollapsedToolItem("Bash", ["ls", "pwd"]).SummaryText);
    }

    [Fact]
    public void IsAggregatable_OnlyReadGrepGlob()
    {
        Assert.True(CollapsedToolItem.IsAggregatable("Read"));
        Assert.True(CollapsedToolItem.IsAggregatable("Grep"));
        Assert.True(CollapsedToolItem.IsAggregatable("Glob"));
        Assert.False(CollapsedToolItem.IsAggregatable("Edit"));
        Assert.False(CollapsedToolItem.IsAggregatable("Bash"));
        Assert.False(CollapsedToolItem.IsAggregatable("Write"));
    }

    [Fact]
    public void ContainsText_MatchesToolNameSummaryAndAnyCallArgs()
    {
        var item = new CollapsedToolItem("Read", ["src/alpha.cs", "src/beta.cs"]);

        Assert.True(item.ContainsText("alpha"));
        Assert.True(item.ContainsText("BETA"));
        Assert.True(item.ContainsText("read"));
        Assert.True(item.ContainsText("读取"));
        Assert.False(item.ContainsText("gamma"));
    }
}
