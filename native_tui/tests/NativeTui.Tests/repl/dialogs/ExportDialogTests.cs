namespace NativeTui.Tests.Repl;

/// <summary>
/// 导出对话对话框测试（Export 对话框域）— 基准 ExportDialog.tsx 选项选择阶段：
/// 状态机（初始焦点 0 / ↑↓ 环绕 / Enter 确认 Closed+Confirmed / Esc 取消 Closed 未确认 /
/// 1-2 数字直选一步确认 / 未消费键 false 透传 / Closed 后防御 no-op）+
/// 渲染骨架（边框 permission 色 / 标题 "导出对话" / 副标题 / 两选项行焦点高亮 /
/// footer 快捷键提示）+ MeasureHeight 固定 9。
/// </summary>
public sealed class ExportDialogTests
{
    /// <summary>footer 快捷键提示期望串（RenderModelShortcuts 1 缩进拼接形态）。</summary>
    private const string ExpectedShortcuts = " ↑↓ 选择 · 1/2 直选 · enter 确认 · esc 取消";

    /// <summary>渲染到 40×9 屏（对话框满高）并返回屏幕。</summary>
    private static Screen RenderToScreen(ExportDialog dialog)
    {
        var screen = new Screen(cols: 60, rows: 9);
        dialog.Render(screen, new Rect(0, 0, 60, 9), Theme.Dark);
        return screen;
    }

    // ---- 状态机 ----

    /// <summary>初始态 — 焦点 0（剪贴板）/ 未关闭 / 未确认 / SelectedTarget 序数直映。</summary>
    [Fact]
    public void Initial_State_ZeroClipboardOpenUnconfirmed()
    {
        var dialog = new ExportDialog();

        Assert.Equal(0, dialog.SelectedIndex);
        Assert.False(dialog.Closed);
        Assert.False(dialog.Confirmed);
        Assert.Equal(ExportTarget.Clipboard, dialog.SelectedTarget);
    }

    /// <summary>↑↓ 焦点环绕（Select 语义）— ↓ 0→1→0 环绕、↑ 0→1；移动只换焦点不关闭。</summary>
    [Fact]
    public void TryHandleKey_DownUp_WrapsSelectionWithoutClosing()
    {
        var dialog = new ExportDialog();

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.Equal(1, dialog.SelectedIndex);
        Assert.Equal(ExportTarget.File, dialog.SelectedTarget);
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.Equal(0, dialog.SelectedIndex);
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.UpArrow)));
        Assert.Equal(1, dialog.SelectedIndex);
        Assert.False(dialog.Closed);
        Assert.False(dialog.Confirmed);
    }

    /// <summary>Enter 确认 — Closed + Confirmed + SelectedTarget 随焦点档（剪贴板）。</summary>
    [Fact]
    public void TryHandleKey_Enter_ConfirmsCurrentSelection()
    {
        var dialog = new ExportDialog();

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Return)));

        Assert.True(dialog.Closed);
        Assert.True(dialog.Confirmed);
        Assert.Equal(ExportTarget.Clipboard, dialog.SelectedTarget);
    }

    /// <summary>焦点移动后 Enter — SelectedTarget 随移动后焦点档（文件）。</summary>
    [Fact]
    public void TryHandleKey_DownThenEnter_SelectsFileTarget()
    {
        var dialog = new ExportDialog();

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Return)));

        Assert.True(dialog.Confirmed);
        Assert.Equal(ExportTarget.File, dialog.SelectedTarget);
    }

    /// <summary>Esc 取消 — Closed 未确认（Confirmed 保持 false，宿主静默收尾不执行 IO）。</summary>
    [Fact]
    public void TryHandleKey_Escape_CancelsWithoutConfirming()
    {
        var dialog = new ExportDialog();

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Escape)));

        Assert.True(dialog.Closed);
        Assert.False(dialog.Confirmed);
    }

    /// <summary>数字直选 1 — 焦点定位剪贴板档并一步确认（对齐权限对话框数字选档语义）。</summary>
    [Fact]
    public void TryHandleKey_Digit1_DirectSelectsClipboard()
    {
        var dialog = new ExportDialog();
        dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));

        Assert.True(dialog.TryHandleKey(KeyPress.Char("1")));

        Assert.Equal(0, dialog.SelectedIndex);
        Assert.True(dialog.Closed);
        Assert.True(dialog.Confirmed);
        Assert.Equal(ExportTarget.Clipboard, dialog.SelectedTarget);
    }

    /// <summary>数字直选 2 — 焦点定位文件档并一步确认。</summary>
    [Fact]
    public void TryHandleKey_Digit2_DirectSelectsFile()
    {
        var dialog = new ExportDialog();

        Assert.True(dialog.TryHandleKey(KeyPress.Char("2")));

        Assert.Equal(1, dialog.SelectedIndex);
        Assert.True(dialog.Closed);
        Assert.True(dialog.Confirmed);
        Assert.Equal(ExportTarget.File, dialog.SelectedTarget);
    }

    /// <summary>未消费键 — 普通字符 / Tab / 方向左右 → false 透传宿主且状态不变。</summary>
    [Fact]
    public void TryHandleKey_UnconsumedKeys_ReturnFalse()
    {
        var dialog = new ExportDialog();

        Assert.False(dialog.TryHandleKey(KeyPress.Char("x")));
        Assert.False(dialog.TryHandleKey(KeyPress.Named(KeyKind.Tab)));
        Assert.False(dialog.TryHandleKey(KeyPress.Named(KeyKind.LeftArrow)));

        Assert.Equal(0, dialog.SelectedIndex);
        Assert.False(dialog.Closed);
    }

    /// <summary>Closed 后按键 — 恒 false 且状态不再流转（宿主应已移除，防御性 no-op）。</summary>
    [Fact]
    public void TryHandleKey_AfterClosed_AlwaysFalse()
    {
        var dialog = new ExportDialog();
        dialog.TryHandleKey(KeyPress.Named(KeyKind.Return));

        Assert.False(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.False(dialog.TryHandleKey(KeyPress.Named(KeyKind.Return)));
        Assert.False(dialog.TryHandleKey(KeyPress.Char("2")));
        Assert.True(dialog.Confirmed);
        Assert.Equal(ExportTarget.Clipboard, dialog.SelectedTarget);
    }

    // ---- 渲染 ----

    /// <summary>固定行序骨架 — margin / 边框全宽 / 标题 / 副标题 / 空行 / 两选项 / 空行 / footer。</summary>
    [Fact]
    public void Render_FixedRowSkeleton()
    {
        using var screen = RenderToScreen(new ExportDialog());

        Assert.Equal(new string('─', 60), RowText.Of(screen, 1, 0, 60));
        Assert.Equal(" 导出对话", RowText.Of(screen, 2, 0, 60).TrimEnd());
        Assert.Equal(" 选择导出方式：", RowText.Of(screen, 3, 0, 60).TrimEnd());
        Assert.Equal(string.Empty, RowText.Of(screen, 4, 0, 60).TrimEnd());
        Assert.Equal("❯ 1. 复制到剪贴板", RowText.Of(screen, 5, 0, 60).TrimEnd());
        Assert.Equal("  2. 保存为 Markdown 文件", RowText.Of(screen, 6, 0, 60).TrimEnd());
        Assert.Equal(string.Empty, RowText.Of(screen, 7, 0, 60).TrimEnd());
        Assert.Equal(ExpectedShortcuts, RowText.Of(screen, 8, 0, 60).TrimEnd());
    }

    /// <summary>首项焦点高亮 — ❯ suggestion 色 + label suggestion（RenderPermissionOption 焦点形态）。</summary>
    [Fact]
    public void Render_FocusedOption_SuggestionStyledPointer()
    {
        using var screen = RenderToScreen(new ExportDialog());

        var suggestion = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Suggestion });
        Assert.Equal(new Rune('❯'), screen[5, 0].Value);
        Assert.Equal(suggestion, screen[5, 0].StyleId);
        Assert.Equal(suggestion, screen[5, 5].StyleId);
    }

    /// <summary>次项非焦点 — 空格指示 + label 默认样式（无 ❯）。</summary>
    [Fact]
    public void Render_UnfocusedOption_PlainIndicator()
    {
        using var screen = RenderToScreen(new ExportDialog());

        Assert.Equal(new Rune(' '), screen[6, 0].Value);
        Assert.Equal(0, screen[6, 5].StyleId);
    }

    /// <summary>↓ 后高亮切换 — ❯ 移到第二项，第一项还原空格指示。</summary>
    [Fact]
    public void Render_AfterDown_HighlightMovesToSecondOption()
    {
        var dialog = new ExportDialog();
        dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));
        using var screen = RenderToScreen(dialog);

        Assert.Equal(new Rune(' '), screen[5, 0].Value);
        Assert.Equal(new Rune('❯'), screen[6, 0].Value);
        Assert.Equal("  1. 复制到剪贴板", RowText.Of(screen, 5, 0, 60).TrimEnd());
        Assert.Equal("❯ 2. 保存为 Markdown 文件", RowText.Of(screen, 6, 0, 60).TrimEnd());
    }

    /// <summary>MeasureHeight — 固定 9（margin + 边框 + 标题 + 副标题 + 空行 + 选项 2 + 空行 + footer）。</summary>
    [Fact]
    public void MeasureHeight_IsFixedNine()
    {
        Assert.Equal(9, new ExportDialog().MeasureHeight(40));
        Assert.Equal(9, new ExportDialog().MeasureHeight(80));
    }

    /// <summary>Closed 后渲染 — 防御性零绘制（无边框 — 宿主应已移除本项）。</summary>
    [Fact]
    public void Render_AfterClosed_DrawsNothing()
    {
        var dialog = new ExportDialog();
        dialog.TryHandleKey(KeyPress.Named(KeyKind.Escape));
        using var screen = RenderToScreen(dialog);

        Assert.Equal(new Rune(' '), screen[1, 0].Value);
        Assert.Equal(string.Empty, RowText.Of(screen, 2, 0, 60).TrimEnd());
    }
}
