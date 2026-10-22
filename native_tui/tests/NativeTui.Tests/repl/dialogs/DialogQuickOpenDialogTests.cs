namespace NativeTui.Tests.Repl;

/// <summary>
/// QuickOpenDialog 状态机测试（QuickOpen 对话框域 — 键位路由/查询过滤/选中导航/接受关闭契约）：
/// 空查询全量排序（路径短优先+字母序）/ 上限 20 截断 / 字符与粘贴进查询并重过滤 / 退格去尾 /
/// ↑↓ 与 ctrl+p/ctrl+n clamp 导航 + 滚动窗口跟随 / 查询变化选中归零 / enter 接受焦点项 /
/// esc 关闭置空 / 模态吞键（Tab 等）/ 已关闭拒键 / null 清单契约。
/// </summary>
public sealed class DialogQuickOpenDialogTests
{
    /// <summary>空查询 — 全量展示（空查询同分 → 路径短优先 + 字母序，对齐 AtFileSuggester 排序语义）。</summary>
    [Fact]
    public void Construct_EmptyQuery_RanksShortestPathFirstThenOrdinal()
    {
        var dialog = new QuickOpenDialog(["docs/adr/0137.md", "README.md", "src/Program.cs"]);

        Assert.Equal(string.Empty, dialog.Query);
        Assert.Equal(["README.md", "src/Program.cs", "docs/adr/0137.md"], dialog.Results);
        Assert.Equal(0, dialog.SelectedIndex);
        Assert.False(dialog.IsClosed);
        Assert.Null(dialog.AcceptedFile);
    }

    /// <summary>超过上限 — 截断到 MaxResults（20）。</summary>
    [Fact]
    public void Construct_MoreThanMaxResults_Truncates()
    {
        var files = Enumerable.Range(1, 30).Select(i => $"file{i:D2}.txt").ToArray();
        var dialog = new QuickOpenDialog(files);

        Assert.Equal(QuickOpenDialog.MaxResults, dialog.Results.Count);
        Assert.Equal("file01.txt", dialog.Results[0]);
        Assert.Equal("file20.txt", dialog.Results[^1]);
    }

    /// <summary>字符输入 — 进查询并按 FuzzyMatcher 过滤。</summary>
    [Fact]
    public void TypeChars_FiltersByFuzzyMatch()
    {
        var dialog = new QuickOpenDialog(
            ["src/Program.cs", "src/NativeTui.Core/ReplShell.cs", "README.md", "docs/adr/0137.md"]);

        foreach (var character in "repl")
        {
            Assert.True(dialog.TryHandleKey(KeyPress.Char(character.ToString())));
        }

        Assert.Equal("repl", dialog.Query);
        Assert.Equal(["src/NativeTui.Core/ReplShell.cs"], dialog.Results);
        Assert.Equal("src/NativeTui.Core/ReplShell.cs", dialog.SelectedFile);
    }

    /// <summary>无匹配查询 — 结果清空且选中态防御（SelectedFile null）。</summary>
    [Fact]
    public void TypeChars_NoMatch_EmptyResults()
    {
        var dialog = new QuickOpenDialog(["README.md"]);

        Assert.True(dialog.TryHandleKey(KeyPress.Char("z")));
        Assert.True(dialog.TryHandleKey(KeyPress.Char("q")));

        Assert.Equal("zq", dialog.Query);
        Assert.Empty(dialog.Results);
        Assert.Null(dialog.SelectedFile);
    }

    /// <summary>粘贴块 — Input 原文整段进查询（KeyKind.Paste 携带多字符载荷）。</summary>
    [Fact]
    public void Paste_AppendsToQuery()
    {
        var dialog = new QuickOpenDialog(["src/App.cs", "README.md"]);

        Assert.True(dialog.TryHandleKey(new KeyPress(KeyKind.Paste, "app")));

        Assert.Equal("app", dialog.Query);
        Assert.Equal(["src/App.cs"], dialog.Results);
    }

    /// <summary>退格 — 去尾并重过滤（多级退格回到空查询恢复全量）。</summary>
    [Fact]
    public void Backspace_RemovesLastChar_AndRefilters()
    {
        var dialog = new QuickOpenDialog(["src/App.cs", "src/AppConfig.cs", "README.md"]);

        foreach (var character in "app")
        {
            dialog.TryHandleKey(KeyPress.Char(character.ToString()));
        }

        Assert.Equal(2, dialog.Results.Count);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.Backspace));

        Assert.Equal("ap", dialog.Query);
        Assert.Equal(2, dialog.Results.Count);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.Backspace));
        dialog.TryHandleKey(KeyPress.Named(KeyKind.Backspace));

        Assert.Equal(string.Empty, dialog.Query);
        Assert.Equal(3, dialog.Results.Count);
    }

    /// <summary>空查询退格 — 静默吞掉不关闭（基准 backspaceExitsOnEmpty=false）。</summary>
    [Fact]
    public void Backspace_OnEmptyQuery_StaysOpenAndSwallows()
    {
        var dialog = new QuickOpenDialog(["README.md"]);

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Backspace)));

        Assert.False(dialog.IsClosed);
        Assert.Equal(string.Empty, dialog.Query);
    }

    /// <summary>↑↓ 导航 — clamp 不环绕（顶部/底部边界钳制）。</summary>
    [Fact]
    public void ArrowKeys_MoveSelection_ClampedAtBounds()
    {
        var dialog = new QuickOpenDialog(["a.cs", "b.cs", "c.cs"]);

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.UpArrow)));
        Assert.Equal(0, dialog.SelectedIndex);

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.Equal(2, dialog.SelectedIndex);

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.Equal(2, dialog.SelectedIndex);
    }

    /// <summary>ctrl+p / ctrl+n — 上下导航别名（基准 FuzzyPicker emacs 键位）。</summary>
    [Fact]
    public void CtrlPAndCtrlN_NavigateSelection()
    {
        var dialog = new QuickOpenDialog(["a.cs", "b.cs", "c.cs"]);

        Assert.True(dialog.TryHandleKey(new KeyPress(KeyKind.Char, "n", Ctrl: true)));
        Assert.Equal(1, dialog.SelectedIndex);

        Assert.True(dialog.TryHandleKey(new KeyPress(KeyKind.Char, "p", Ctrl: true)));
        Assert.Equal(0, dialog.SelectedIndex);
    }

    /// <summary>查询变化 — 选中归零（对齐基准 FuzzyPicker setFocusedIndex(0)）。</summary>
    [Fact]
    public void QueryChange_ResetsSelectionToFirst()
    {
        var dialog = new QuickOpenDialog(["a.cs", "b.cs", "readme.txt"]);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));
        Assert.Equal(1, dialog.SelectedIndex);

        dialog.TryHandleKey(KeyPress.Char("r"));
        dialog.TryHandleKey(KeyPress.Char("e"));

        Assert.Equal(0, dialog.SelectedIndex);
        Assert.Equal(["readme.txt"], dialog.Results);
    }

    /// <summary>导航越界可见窗口 — 滚动窗口跟随焦点（对齐 ModelPickerCoordinator 窗口语义）。</summary>
    [Fact]
    public void Navigation_BeyondVisible_ScrollsWindow()
    {
        var files = Enumerable.Range(1, 12).Select(i => $"f{i:D2}.txt").ToArray();
        var dialog = new QuickOpenDialog(files);

        Assert.Equal(0, dialog.WindowStart);
        Assert.Equal(4, dialog.HiddenCount);

        for (var i = 0; i < 8; i++)
        {
            dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));
        }

        Assert.Equal(8, dialog.SelectedIndex);
        Assert.Equal(1, dialog.WindowStart);
    }

    /// <summary>enter — 接受焦点项（AcceptedFile 契约属性）并关闭。</summary>
    [Fact]
    public void Enter_AcceptsFocusedFile_AndCloses()
    {
        var dialog = new QuickOpenDialog(["a.cs", "b.cs", "c.cs"]);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));
        dialog.TryHandleKey(KeyPress.Named(KeyKind.Return));

        Assert.True(dialog.IsClosed);
        Assert.Equal("b.cs", dialog.AcceptedFile);
    }

    /// <summary>enter 无结果 — 接受 null 并关闭（宿主据此无操作）。</summary>
    [Fact]
    public void Enter_WithNoResults_AcceptsNull_AndCloses()
    {
        var dialog = new QuickOpenDialog(["a.cs"]);

        dialog.TryHandleKey(KeyPress.Char("z"));
        dialog.TryHandleKey(KeyPress.Named(KeyKind.Return));

        Assert.True(dialog.IsClosed);
        Assert.Null(dialog.AcceptedFile);
    }

    /// <summary>esc — 关闭且接受置空（取消语义）。</summary>
    [Fact]
    public void Escape_ClosesWithNullAcceptedFile()
    {
        var dialog = new QuickOpenDialog(["a.cs", "b.cs"]);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));
        dialog.TryHandleKey(KeyPress.Named(KeyKind.Escape));

        Assert.True(dialog.IsClosed);
        Assert.Null(dialog.AcceptedFile);
    }

    /// <summary>未接线键 — 模态吞掉（Tab/Delete/Home/方向横移等），查询与状态不变。</summary>
    [Fact]
    public void UnhandledKeys_AreSwallowedModally()
    {
        var dialog = new QuickOpenDialog(["a.cs"]);

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Tab)));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Delete)));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.LeftArrow)));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Home)));
        Assert.True(dialog.TryHandleKey(new KeyPress(KeyKind.Char, "x", Ctrl: true)));

        Assert.Equal(string.Empty, dialog.Query);
        Assert.False(dialog.IsClosed);
        Assert.Single(dialog.Results);
    }

    /// <summary>已关闭 — 拒绝后续键（TryHandleKey false 防御契约，状态不再变化）。</summary>
    [Fact]
    public void ClosedDialog_RejectsFurtherKeys()
    {
        var dialog = new QuickOpenDialog(["a.cs"]);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.Escape));

        Assert.False(dialog.TryHandleKey(KeyPress.Named(KeyKind.Return)));
        Assert.Null(dialog.AcceptedFile);
        Assert.True(dialog.IsClosed);
    }

    /// <summary>null 清单 — 构造契约抛 ArgumentNullException。</summary>
    [Fact]
    public void Construct_NullFiles_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new QuickOpenDialog(null!));
    }
}
