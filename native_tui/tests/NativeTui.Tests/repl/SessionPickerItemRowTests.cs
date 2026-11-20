namespace NativeTui.Tests.Repl;

/// <summary>
/// 会话选择器列表项增强测试（TASK069-C1 — 对齐基准 LogSelector 的 label + dim description 两行制）：
/// 每项占 2 行（标题主行 + ID/元数据副行）、可见项数按终端行数推导（基准 visibleOptionCount 高度自适应）、
/// 元数据里的分支与消息数可被搜索命中。
/// </summary>
public sealed class SessionPickerItemRowTests
{
    private static SessionPickerItem[] Items() =>
    [
        new("8f2a1c9d-1111-2222-3333-444455556666", "fix login redirect", "3 分钟前 · main · 12 条消息"),
        new("33bb77aa-1111-2222-3333-444455556666", "refactor parser", "2 小时前 · feature/x · 40 条消息"),
        new("cc44dd55-1111-2222-3333-444455556666", "release notes", "3 天前 · main · 21 条消息"),
    ];

    [Theory]
    [InlineData(24, 8)]  // 常见 80x24：(24 - 7 固定 - 1 溢出) / 2 行每项
    [InlineData(40, 16)]
    [InlineData(12, 2)]
    [InlineData(9, 1)]   // 极矮终端仍保留 1 项（不出现「一项都看不见」）
    [InlineData(8, 1)]
    [InlineData(0, 8)]   // 高度未知 → 按 24 行兜底
    public void MaxVisible_IsDerivedFromTerminalRows(int terminalRows, int expected)
    {
        Assert.Equal(expected, SessionPickerDialog.MaxVisibleFor(terminalRows));
    }

    [Fact]
    public void ItemOccupiesTwoRows()
    {
        var dialog = new SessionPickerDialog(Items(), "选择会话", terminalRows: 40);

        // margin + 边框 + 标题 + 查询 + 空行 + 空行 + footer = 7，列表 3 项 × 2 行
        Assert.Equal(13, dialog.MeasureHeight(60));
    }

    [Fact]
    public void TitleAndMetadata_RenderOnSeparateRows()
    {
        using var screen = new Screen(cols: 60, rows: 20);
        var dialog = new SessionPickerDialog(Items(), "选择会话", terminalRows: 20);

        dialog.Render(screen, new Rect(0, 0, 60, 20), Theme.Dark);

        // 列表自第 5 行起；焦点项主行带 ❯，副行 4 空格缩进 dim「[ID前8位] 元数据」
        var title = RowText.Of(screen, 5, 0, 60);
        Assert.Contains("fix login redirect", title);
        Assert.StartsWith("❯", title);
        Assert.DoesNotContain("条消息", title);

        var metadata = RowText.Of(screen, 6, 0, 60);
        Assert.Contains("[8f2a1c9d] 3 分钟前 · main · 12 条消息", metadata);

        // 第二项（非焦点）同形：主行无 ❯，副行同样带 ID 与元数据
        Assert.Contains("refactor parser", RowText.Of(screen, 7, 0, 60));
        Assert.Contains("[33bb77aa] 2 小时前 · feature/x · 40 条消息", RowText.Of(screen, 8, 0, 60));
    }

    [Fact]
    public async Task MetadataBranch_IsSearchable()
    {
        var dialog = new SessionPickerDialog(Items(), "选择会话", terminalRows: 40);

        foreach (var ch in "feature")
        {
            dialog.TryHandleKey(KeyPress.Char(ch.ToString()));
        }

        Assert.Equal(1, dialog.FilteredCount);
        dialog.TryHandleKey(KeyPress.Named(KeyKind.Return));
        Assert.Equal("33bb77aa-1111-2222-3333-444455556666", await dialog.SelectionTask);
    }

    [Fact]
    public void ShortTerminal_ClampsVisibleItemsAndReportsOverflow()
    {
        var dialog = new SessionPickerDialog(Items(), "选择会话", terminalRows: 9);

        Assert.Equal(1, dialog.MaxVisible);
        Assert.Equal(1, dialog.VisibleCount);
        Assert.Equal(2, dialog.HiddenCount);
        // 7 固定 + 1 项 2 行 + 1 行溢出提示
        Assert.Equal(10, dialog.MeasureHeight(60));
    }
}
