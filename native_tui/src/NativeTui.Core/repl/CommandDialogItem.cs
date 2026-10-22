namespace NativeTui.Core.Repl;

/// <summary>
/// 命令交互对话框消息项（TASK065 F2）— 斜杠命令 confirm/prompt 挂起时插入滚动区：
/// 视觉对齐 PermissionRequestItem（顶边框 permission 色 + bold 标题 + 3 缩进消息行 +
/// Select 数字前缀选项 + Esc 取消提示），confirm 两档（1.Yes / 2.No 焦点 ❯ suggestion）；
/// prompt 形态为输入行（❯ suggestion + 输入缓冲 + 行尾反色光标块）。状态源 =
/// CommandDialogCoordinator（消息/焦点/输入缓冲），Begin 时插入 Messages，Complete 时移除。
/// </summary>
public sealed class CommandDialogItem(CommandDialogCoordinator coordinator) : IScrollItem
{
    /// <summary>固定标题（对齐权限对话框 "Tool use" 的领域命名形态）。</summary>
    private const string Title = "Command";

    /// <summary>confirm 两档选项标签。</summary>
    private static readonly string[] ConfirmOptionLabels = ["Yes", "No"];

    /// <summary>协调器（消息/焦点/输入状态的唯一来源）。</summary>
    public CommandDialogCoordinator Coordinator { get; } = coordinator;

    /// <summary>内容高度 = margin1 + 边框1 + 标题1 + 空行1 + 消息1 + 空行1 +
    /// confirm 选项2 + Esc margin1 + Esc1（=10）/ prompt 输入1 + Esc margin1 + Esc1（=9）。</summary>
    public int MeasureHeight(int width) =>
        Coordinator.IsPrompt ? 9 : 10;

    /// <summary>渲染：按固定行序绘制（状态由 Coordinator 投影 — 挂起被清除时只画空 margin）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var message = Coordinator.Message;
        if (message is null)
        {
            return;
        }

        var row = frame.Y + 1;
        MessageRenderer.RenderPermissionBorder(screen, RowFrame(frame, row++), theme);
        MessageRenderer.RenderPermissionTitle(screen, RowFrame(frame, row++), theme, Title);
        row++;
        MessageRenderer.RenderPermissionDescription(screen, RowFrame(frame, row++), theme, message);
        row++;
        if (Coordinator.IsPrompt)
        {
            RenderPromptInputRow(screen, RowFrame(frame, row++), theme);
        }
        else
        {
            for (var i = 0; i < CommandDialogCoordinator.ConfirmOptionCount; i++)
            {
                MessageRenderer.RenderPermissionOption(screen, RowFrame(frame, row++), theme, i, ConfirmOptionLabels[i], i == Coordinator.ConfirmFocusIndex);
            }
        }

        row++;
        MessageRenderer.RenderPermissionEscHint(screen, RowFrame(frame, row), theme);
    }

    /// <summary>prompt 输入行 — 3 缩进 ❯ suggestion + 输入缓冲 + 行尾反色光标块（输入光标语义）。</summary>
    private void RenderPromptInputRow(Screen screen, Rect frame, Theme theme)
    {
        var suggestion = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Suggestion });
        var col = screen.WriteText(frame.Y, frame.X, "   ❯ ", suggestion, frame.Right);
        col = screen.WriteText(frame.Y, col, Coordinator.PromptInput, 0, frame.Right);
        if (col < frame.Right)
        {
            var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
            screen[frame.Y, col] = new Cell(new Rune(' '), inverse, CellWidth.Default);
        }
    }

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };
}
