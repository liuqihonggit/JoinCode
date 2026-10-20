namespace NativeTui.Core.Repl;

/// <summary>
/// 权限对话框消息项（M7-3）— 对齐基准 FallbackPermissionRequest 渲染结构
///（PermissionDialog 顶边框 + PermissionRequestTitle 标题 + 工具/描述行 + PermissionPrompt 问题与选项 + Esc 提示）：
/// margin 1 + 顶边框（permission 色全宽，仅 top 无角）+ 标题（1 缩进 bold permission "Tool use"）
/// + paddingY 空行 + 工具行（3 缩进 bold 名 + (描述)）+ 描述行（3 缩进 dim，空描述折叠）
/// + paddingY 空行 + 问题行（"Do you want to proceed?"）+ 三档数字前缀选项（焦点 ❯ suggestion）
/// + Esc 提示 margin + "Esc to cancel" dim。
/// 状态源 = PermissionCoordinator（焦点/请求），Begin 时插入 Messages，Complete 时移除。
/// </summary>
public sealed class PermissionRequestItem(PermissionCoordinator coordinator) : IScrollItem
{
    /// <summary>固定标题（基准 PermissionDialog title="Tool use"）。</summary>
    private const string Title = "Tool use";

    /// <summary>三档选项标签（对齐基准 yes / yes-dont-ask-again / no；dont-ask 文案合并 toolName 语境简化为固定串）。</summary>
    private static readonly string[] OptionLabels =
    [
        "Yes",
        "Yes, and don't ask again for this tool",
        "No",
    ];

    /// <summary>协调器（焦点与请求状态的唯一来源）。</summary>
    public PermissionCoordinator Coordinator { get; } = coordinator;

    /// <summary>内容高度 = margin 1 + 边框 1 + 标题 1 + 空行 1 + 工具 1 + 描述(空则 0) + 空行 1 + 问题 1 + 选项 3 + Esc margin 1 + Esc 1。</summary>
    public int MeasureHeight(int width) =>
        12 + (string.IsNullOrEmpty(Coordinator.Request?.Description) ? 0 : 1);

    /// <summary>渲染：按固定行序绘制（状态由 Coordinator 投影 — 挂起被清除时只画空 margin）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var request = Coordinator.Request;
        if (request is null)
        {
            return;
        }

        var row = frame.Y + 1;
        Row(screen, frame, theme, row++, MessageRenderer.RenderPermissionBorder);
        MessageRenderer.RenderPermissionTitle(screen, RowFrame(frame, row++), theme, Title);
        row++;
        MessageRenderer.RenderPermissionToolLine(screen, RowFrame(frame, row++), theme, request.ToolName, request.Description);
        if (!string.IsNullOrEmpty(request.Description))
        {
            MessageRenderer.RenderPermissionDescription(screen, RowFrame(frame, row++), theme, request.Description);
        }

        row++;
        MessageRenderer.RenderPermissionQuestion(screen, RowFrame(frame, row++), theme);
        for (var i = 0; i < PermissionCoordinator.OptionCount; i++)
        {
            MessageRenderer.RenderPermissionOption(screen, RowFrame(frame, row++), theme, i, OptionLabels[i], i == Coordinator.FocusIndex);
        }

        row++;
        MessageRenderer.RenderPermissionEscHint(screen, RowFrame(frame, row), theme);
    }

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };

    /// <summary>无边框参数的单行渲染转发。</summary>
    private static void Row(Screen screen, Rect frame, Theme theme, int row, Action<Screen, Rect, Theme> render) =>
        render(screen, RowFrame(frame, row), theme);
}
