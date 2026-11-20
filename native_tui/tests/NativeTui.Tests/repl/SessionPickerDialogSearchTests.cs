namespace NativeTui.Tests.Repl;

/// <summary>
/// 会话选择器模糊搜索测试（TASK069-C2 — 与 QuickOpenDialog 同一 FuzzyMatcher 数据层）：
/// 输入即过滤（命中降序、同分原序）、退格还原、零命中提示、查询变化焦点与窗口归零、
/// 选中项取自过滤结果（enter 回传的是看得见的那一条）。
/// </summary>
public sealed class SessionPickerDialogSearchTests
{
    private static SessionPickerItem[] Items() =>
    [
        new("8f2a1c9d", "fix login redirect", "3 分钟前"),
        new("33bb77aa", "refactor markdown parser", "2 小时前"),
        new("aa11bb22", "login retry backoff", "昨天"),
        new("cc44dd55", "release notes", "3 天前"),
    ];

    private static SessionPickerDialog Create() => new(Items(), "选择会话（当前仓库）");

    [Fact]
    public void TypingFiltersItemsByPreview()
    {
        var dialog = Create();

        foreach (var ch in "parser")
        {
            dialog.TryHandleKey(KeyPress.Char(ch.ToString()));
        }

        Assert.Equal("parser", dialog.Query);
        Assert.Equal(1, dialog.FilteredCount);
        Assert.Equal(1, dialog.VisibleCount);
    }

    [Fact]
    public void BackspaceRestoresWiderResultSet()
    {
        var dialog = Create();
        foreach (var ch in "login")
        {
            dialog.TryHandleKey(KeyPress.Char(ch.ToString()));
        }

        Assert.Equal(2, dialog.FilteredCount); // fix login redirect + login retry backoff

        for (var i = 0; i < "login".Length; i++)
        {
            dialog.TryHandleKey(KeyPress.Named(KeyKind.Backspace));
        }

        Assert.Equal(string.Empty, dialog.Query);
        Assert.Equal(4, dialog.FilteredCount);
        Assert.Equal(4, dialog.VisibleCount);
    }

    [Fact]
    public void NoMatch_ShowsHintInsteadOfCrashingRows()
    {
        using var screen = new Screen(cols: 40, rows: 9);
        var dialog = Create();
        foreach (var ch in "zzz")
        {
            dialog.TryHandleKey(KeyPress.Char(ch.ToString()));
        }

        Assert.Equal(0, dialog.FilteredCount);
        // margin + 边框 + 标题 + 查询 + 空行 + 空态 1 行 + 空行 + footer
        Assert.Equal(8, dialog.MeasureHeight(40));
        dialog.Render(screen, new Rect(0, 0, 40, 9), Theme.Dark);

        Assert.Contains("无匹配会话", RowText.Of(screen, 5, 0, 40));
    }

    [Fact]
    public void QueryChange_ResetsSelectionAndWindow()
    {
        var dialog = Create();
        dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));
        dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));
        Assert.Equal(2, dialog.SelectedIndex);

        dialog.TryHandleKey(KeyPress.Char("l"));

        Assert.Equal(0, dialog.SelectedIndex);
        Assert.Equal(0, dialog.WindowStart);
    }

    [Fact]
    public async Task EnterSelectsFilteredItem()
    {
        var dialog = Create();
        foreach (var ch in "release")
        {
            dialog.TryHandleKey(KeyPress.Char(ch.ToString()));
        }

        dialog.TryHandleKey(KeyPress.Named(KeyKind.Return));

        Assert.True(dialog.IsClosed);
        Assert.Equal("cc44dd55", await dialog.SelectionTask);
    }

    [Fact]
    public async Task Escape_CancelsRegardlessOfQuery()
    {
        var dialog = Create();
        dialog.TryHandleKey(KeyPress.Char("x"));

        dialog.TryHandleKey(KeyPress.Named(KeyKind.Escape));

        Assert.True(dialog.IsClosed);
        Assert.Null(await dialog.SelectionTask);
    }

    [Fact]
    public void QueryRow_ShowsPlaceholderThenText()
    {
        using var screen = new Screen(cols: 40, rows: 9);
        var dialog = Create();
        dialog.Render(screen, new Rect(0, 0, 40, 9), Theme.Dark);
        Assert.Contains("输入以搜索会话…", RowText.Of(screen, 3, 0, 40));

        dialog.TryHandleKey(KeyPress.Char("r"));
        dialog.TryHandleKey(KeyPress.Char("e"));

        using var second = new Screen(cols: 40, rows: 9);
        dialog.Render(second, new Rect(0, 0, 40, 9), Theme.Dark);
        Assert.Contains("❯ re", RowText.Of(second, 3, 0, 40));
    }
}
