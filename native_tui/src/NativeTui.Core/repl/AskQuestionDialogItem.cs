namespace NativeTui.Core.Repl;

/// <summary>
/// TUI 问答对话框消息项 — 问答挂起时插入滚动区：顶边框 permission 色 + "Question i/n" 标题 +
/// Header/问题描述 + 选项（焦点 ❯ suggestion；多选带 [x]/[ ] 前缀）+ 操作提示。
/// 状态源 = AskQuestionCoordinator，Begin 插入，提交/取消/后台回调关闭时移除。
/// </summary>
public sealed class AskQuestionDialogItem(AskQuestionCoordinator coordinator) : IScrollItem
{
    /// <summary>协调器（问题/焦点/选择状态的唯一来源）。</summary>
    public AskQuestionCoordinator Coordinator { get; } = coordinator;

    /// <summary>内容高度 = margin1 + border1 + title1 + header1 + 问题1 + blank1 + 选项N + blank1 + hint1。</summary>
    public int MeasureHeight(int width) => 8 + (Coordinator.Current?.Options.Count ?? 0);

    /// <summary>渲染：按固定行序绘制（挂起被清除时只画空 margin）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        if (Coordinator.Current is not { } current)
        {
            return;
        }

        var row = frame.Y + 1;
        MessageRenderer.RenderPermissionBorder(screen, RowFrame(frame, row++), theme);
        MessageRenderer.RenderPermissionTitle(screen, RowFrame(frame, row++), theme,
            $"Question {Coordinator.QuestionIndex + 1}/{Coordinator.Questions.Count}");
        MessageRenderer.RenderPermissionDescription(screen, RowFrame(frame, row++), theme, current.Header);
        MessageRenderer.RenderPermissionDescription(screen, RowFrame(frame, row++), theme, current.Question);
        row++;

        for (var i = 0; i < current.Options.Count; i++)
        {
            var option = current.Options[i];
            var label = current.MultiSelect
                ? $"{(Coordinator.SelectedIndices.Contains(i) ? "[x]" : "[ ]")} {option.Label}"
                : option.Label;
            MessageRenderer.RenderPermissionOption(screen, RowFrame(frame, row++), theme, i, label, i == Coordinator.FocusIndex);
        }

        row++;
        RenderHint(screen, RowFrame(frame, row), theme, current.MultiSelect);
    }

    /// <summary>操作提示 — 单选/多选提示不同，统一 dim。</summary>
    private static void RenderHint(Screen screen, Rect frame, Theme theme, bool multiSelect)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var hint = multiSelect
            ? " ↑/↓ 移动 · Space 切换 · Enter 确认 · Esc 取消"
            : " ↑/↓ 移动 · Enter 确认 · Esc 取消";
        screen.WriteText(frame.Y, frame.X, hint, dim, frame.Right);
    }

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };
}
