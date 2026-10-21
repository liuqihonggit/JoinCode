namespace NativeTui.Tests.Repl;

/// <summary>
/// 工具分组聚合渲染与小终端塌缩测试（S2 工具折叠渲染域）—
/// CollapsedToolGroupItem：摘要行（● + 文案 + 活动态 "…" + (ctrl+o)，完成态整行 dim）、
/// hint 行（⎿ 形态，活动态且有值）、hint 700ms 不切换（item 帧拍时钟驱动）、
/// ● 600ms 方波闪烁相位；CondensedToolProgressItem：塌缩单行（⎿ + dim 文案 + bold 计数）；
/// ToolFoldingPolicy：VisibleOutputLines 换算表（0/差1/≥2 三档）+ ShouldCondense 边界。
/// </summary>
public sealed class ToolRenderCollapsedGroupTests
{
    /// <summary>构造已聚合的聚合器（Read×2 + Grep×1，hint 已结算显示）。</summary>
    private static ToolGroupAggregator BuildAggregator(string firstReadPath = "a.cs")
    {
        var aggregator = new ToolGroupAggregator();
        aggregator.OnToolCall("Read", $$"""{"file_path":"{{firstReadPath}}"}""");
        aggregator.OnToolCall("Read", """{"file_path":"b.cs"}""");
        aggregator.OnToolCall("Grep", """{"pattern":"TODO"}""");
        aggregator.OnFrame(50);
        return aggregator;
    }

    [Fact]
    public void ActiveGroup_SummaryRowWithEllipsisAndCtrlO()
    {
        // 活动态摘要行 — ● + 摘要 + "…" + dim " (ctrl+o)"（基准 active 组 ToolUseLoader + ellipsis + CtrlOToExpand）
        using var screen = new Screen(cols: 60, rows: 6);
        var item = new CollapsedToolGroupItem(BuildAggregator()) { AnimTimeMs = 0 };

        Assert.Equal(3, item.MeasureHeight(60));
        item.Render(screen, new Rect(0, 0, 60, 6), Theme.Dark);

        Assert.Equal("● 读取 2 个文件 · 搜索 1 个模式… (ctrl+o)", RowText.Of(screen, 1, 0, 42).TrimEnd());
    }

    [Fact]
    public void ActiveGroup_HintRowUsesToolResultIndent()
    {
        // hint 行 — "  ⎿  " + 最新操作（首 hint 700ms 内保持不切换）
        using var screen = new Screen(cols: 40, rows: 6);
        var aggregator = BuildAggregator();
        var item = new CollapsedToolGroupItem(aggregator) { AnimTimeMs = 100 };

        item.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark);

        Assert.Equal("  ⎿  a.cs", RowText.Of(screen, 2, 0, 11).TrimEnd());
    }

    [Fact]
    public void HintRow_HeldAcrossFrames_Until700MsElapses()
    {
        // 防闪烁渲染集成 — AnimTimeMs 帧拍推进 700ms 内 hint 保持旧值，之后切换新值
        var aggregator = new ToolGroupAggregator();
        aggregator.OnToolCall("Read", """{"file_path":"a.cs"}""");
        aggregator.OnFrame(50);
        aggregator.OnToolCall("Read", """{"file_path":"b.cs"}""");
        var item = new CollapsedToolGroupItem(aggregator);

        item.AnimTimeMs = 600;
        using (var screen = new Screen(cols: 40, rows: 6))
        {
            item.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark);
            Assert.Equal("  ⎿  a.cs", RowText.Of(screen, 2, 0, 11).TrimEnd());
        }

        item.AnimTimeMs = 760;
        using (var screen = new Screen(cols: 40, rows: 6))
        {
            item.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark);
            Assert.Equal("  ⎿  b.cs", RowText.Of(screen, 2, 0, 11).TrimEnd());
        }
    }

    [Fact]
    public void InactiveGroup_DimmedSummary_NoHintRow()
    {
        // 完成态 — 整行 dim、无 "…" 省略号、hint 行消失（基准 dimColor={!isActiveGroup}）
        using var screen = new Screen(cols: 60, rows: 6);
        var aggregator = BuildAggregator();
        aggregator.OnToolResult();
        var item = new CollapsedToolGroupItem(aggregator) { IsActive = false, AnimTimeMs = 100 };

        Assert.Equal(2, item.MeasureHeight(60));
        item.Render(screen, new Rect(0, 0, 60, 6), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("● 读取 2 个文件 · 搜索 1 个模式", RowText.Of(screen, 1, 0, 40).TrimEnd());
        Assert.Equal(dim, screen[1, 0].StyleId);
        Assert.Equal(new Rune(' '), screen[2, 0].Value);
    }

    [Fact]
    public void ActiveBullet_BlinksAt600MsSquareWave()
    {
        // ● 600ms 方波闪烁 — 相位 0 亮（t=0）、相位 1 灭（t=600），文案恒定布局不跳动
        var aggregator = BuildAggregator();
        var item = new CollapsedToolGroupItem(aggregator);

        item.AnimTimeMs = 0;
        using (var screen = new Screen(cols: 40, rows: 6))
        {
            item.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark);
            Assert.Equal(new Rune('●'), screen[1, 0].Value);
        }

        item.AnimTimeMs = 600;
        using (var screen = new Screen(cols: 40, rows: 6))
        {
            item.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark);
            Assert.Equal(new Rune(' '), screen[1, 0].Value);
            Assert.Equal("读取", RowText.Of(screen, 1, 2, 5).TrimEnd());
        }
    }

    [Fact]
    public void NoHint_EmptySummaryStillRenders()
    {
        // 空聚合防御 — 摘要空串渲染行仍占位（渲染方跳过逻辑由 Driver 分组边界保证）
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new CollapsedToolGroupItem(new ToolGroupAggregator()) { AnimTimeMs = 0 };

        Assert.Equal(2, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal(new Rune('●'), screen[1, 0].Value);
    }

    [Fact]
    public void CondensedToolProgress_SingleRowWithBoldCount()
    {
        // 塌缩单行 — "  ⎿  In progress… · 3 tool uses"（前缀+文案 dim，计数 bold）
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new CondensedToolProgressItem(3);

        Assert.Equal(2, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        Assert.Equal("  ⎿  In progress… · 3 tool uses", RowText.Of(screen, 1, 0, 31));
        Assert.Equal(dim, screen[1, 5].StyleId);
        Assert.Equal(bold, screen[1, 20].StyleId);
        Assert.Equal(dim, screen[1, 21].StyleId);
    }

    [Fact]
    public void CondensedToolProgress_SingularCount()
    {
        // 单数形态 — 1 tool use（AgentTool/UI.tsx use/uses）
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new CondensedToolProgressItem(1);

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal("  ⎿  In progress… · 1 tool use", RowText.Of(screen, 1, 0, 30));
    }

    [Fact]
    public void ShouldCondense_BoundaryMath()
    {
        // 塌缩判定 — rows < N*9+7（AgentTool/UI.tsx ESTIMATED_LINES_PER_TOOL=9 + TERMINAL_BUFFER_LINES=7）
        Assert.False(ToolFoldingPolicy.ShouldCondense(rows: 16, inProgressToolCallCount: 1));
        Assert.True(ToolFoldingPolicy.ShouldCondense(rows: 15, inProgressToolCallCount: 1));
        Assert.False(ToolFoldingPolicy.ShouldCondense(rows: 34, inProgressToolCallCount: 3));
        Assert.True(ToolFoldingPolicy.ShouldCondense(rows: 33, inProgressToolCallCount: 3));
    }

    [Theory]
    [InlineData(5, 0, 3, 5)]      // 无折叠 — 全量
    [InlineData(5, 1, 3, 4)]      // 差 1 行特例 — 显示剩余全部（3+1）
    [InlineData(5, 2, 3, 3)]      // ≥2 — 预览上限 3
    [InlineData(15, 5, 10, 10)]   // Write 10 行预览
    [InlineData(2, 99, 3, 2)]     // 防御 — hidden 超总量钳制全显
    public void VisibleOutputLines_ConversionTable(int total, int hidden, int previewLimit, int expected)
    {
        Assert.Equal(expected, ToolFoldingPolicy.VisibleOutputLines(total, hidden, previewLimit));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    public void ShowsCollapseHint_OnlyWhenTwoOrMore(int hiddenLines, bool expected)
    {
        Assert.Equal(expected, ToolFoldingPolicy.ShowsCollapseHint(hiddenLines));
    }

    [Fact]
    public void HiddenLinesHint_TextForm()
    {
        Assert.Equal("… +7 lines (ctrl+o)", ToolFoldingPolicy.HiddenLinesHint(7));
    }
}
