namespace NativeTui.Core.Repl;

/// <summary>
/// 权限对话框消息项（M7-3 + S8 per-tool 形态）— 对齐基准 FallbackPermissionRequest 渲染结构
///（PermissionDialog 顶边框 + PermissionRequestTitle 标题 + 工具/描述行 + PermissionPrompt 问题与选项 + Esc 提示）：
/// margin 1 + 顶边框（permission 色全宽，仅 top 无角）+ 标题（1 缩进 bold permission）
/// + paddingY 空行 + 工具行（3 缩进 bold 名 + (描述)）+ 描述行（3 缩进 dim，空描述折叠）
/// + S8 形态区（Bash：命令全文折行 dim + 危险警告 warning 色 / File：路径 + patch 摘要 +N/-M）
/// + paddingY 空行 + 问题行（"Do you want to proceed?"）+ 三档数字前缀选项（焦点 ❯ suggestion）
/// + Esc 提示 margin + "Esc to cancel" dim（Bash 形态附 ctrl+e 展开详情提示）。
/// 状态源 = PermissionCoordinator（焦点/请求），Begin 时插入 Messages，Complete 时移除；
/// 形态数据 = ToolPermissionForms.Derive（Fallback 时渲染零行为变化 — 既有测试零破坏锚点）。
/// </summary>
public sealed class PermissionRequestItem(PermissionCoordinator coordinator) : IScrollItem
{
    /// <summary>三档选项标签（对齐基准 yes / yes-dont-ask-again / no；dont-ask 文案合并 toolName 语境简化为固定串）。</summary>
    private static readonly string[] OptionLabels =
    [
        "Yes",
        "Yes, and don't ask again for this tool",
        "No",
    ];

    /// <summary>协调器（焦点与请求状态的唯一来源）。</summary>
    public PermissionCoordinator Coordinator { get; } = coordinator;

    /// <summary>内容高度 = Fallback 骨架 12（margin1+边框1+标题1+空1+工具1+空1+问题1+选项3+Esc空1+Esc1）
    /// + 描述(空则 0) + 形态区行数（Bash：命令折行 + 警告 0/1 / File：路径 1 + patch 0/1）。</summary>
    public int MeasureHeight(int width) =>
        12 + DescriptionRows + FormRows(width);

    /// <summary>描述行数（空描述折叠为 0）。</summary>
    private int DescriptionRows => string.IsNullOrEmpty(Coordinator.Request?.Description) ? 0 : 1;

    /// <summary>形态区行数 — 按派生形态（Bash 命令折行数 + 警告行 / File 路径 + patch / Fallback 0）。</summary>
    private int FormRows(int width) => Form(width) switch
    {
        { Kind: PermissionFormKind.Bash } form => form.CommandLines.Length + (form.DangerWarning is null ? 0 : 1),
        { Kind: PermissionFormKind.File } form => 1 + (form.HasPatchSummary ? 1 : 0),
        _ => 0,
    };

    /// <summary>派生形态数据（Request 就地派生 — 生命周期内不变；null 安全空串降级 Fallback）。</summary>
    private ToolPermissionForm Form(int width) =>
        ToolPermissionForms.Derive(Coordinator.Request?.ToolName ?? string.Empty, Coordinator.Request?.Description ?? string.Empty, width);

    /// <summary>渲染：按固定行序绘制（状态由 Coordinator 投影 — 挂起被清除时只画空 margin）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var request = Coordinator.Request;
        if (request is null)
        {
            return;
        }

        var form = ToolPermissionForms.Derive(request.ToolName, request.Description, frame.Width);
        var row = frame.Y + 1;
        Row(screen, frame, theme, row++, MessageRenderer.RenderPermissionBorder);
        MessageRenderer.RenderPermissionTitle(screen, RowFrame(frame, row++), theme, form.Title);
        row++;
        MessageRenderer.RenderPermissionToolLine(screen, RowFrame(frame, row++), theme, request.ToolName, request.Description);
        if (!string.IsNullOrEmpty(request.Description))
        {
            MessageRenderer.RenderPermissionDescription(screen, RowFrame(frame, row++), theme, request.Description);
        }

        RenderFormRegion(screen, frame, theme, ref row, form);

        row++;
        MessageRenderer.RenderPermissionQuestion(screen, RowFrame(frame, row++), theme);
        for (var i = 0; i < PermissionCoordinator.OptionCount; i++)
        {
            MessageRenderer.RenderPermissionOption(screen, RowFrame(frame, row++), theme, i, OptionLabels[i], i == Coordinator.FocusIndex);
        }

        row++;
        RenderEscHint(screen, RowFrame(frame, row), theme, form.Kind);
    }

    /// <summary>形态区渲染（S8）— Bash：命令折行 dim + 危险警告 warning 色 / File：路径 + patch 摘要；Fallback 跳过。</summary>
    private static void RenderFormRegion(Screen screen, Rect frame, Theme theme, ref int row, ToolPermissionForm form)
    {
        if (form.Kind == PermissionFormKind.Bash)
        {
            var commandStyle = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
            foreach (var line in form.CommandLines)
            {
                RenderIndentedLine(screen, RowFrame(frame, row++), commandStyle, line);
            }

            if (form.DangerWarning is { } warning)
            {
                RenderIndentedLine(screen, RowFrame(frame, row++), screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Warning }), warning);
            }
        }
        else if (form.Kind == PermissionFormKind.File)
        {
            RenderIndentedLine(screen, RowFrame(frame, row++), 0, form.FilePath!);
            if (form.HasPatchSummary)
            {
                RenderPatchSummary(screen, RowFrame(frame, row++), theme, form.PatchAdded, form.PatchRemoved);
            }
        }
    }

    /// <summary>缩进行渲染 — 3 空格缩进 + 指定样式整行文本。</summary>
    private static void RenderIndentedLine(Screen screen, Rect frame, int style, string text) =>
        screen.WriteText(frame.Y, frame.X, $"{new string(' ', ToolPermissionForms.CommandIndent)}{text}", style, frame.Right);

    /// <summary>patch 摘要渲染 — "+N" Success 色 + " -M" Error 色（对齐 diff 惯例；删除为 0 时只显示新增）。</summary>
    private static void RenderPatchSummary(Screen screen, Rect frame, Theme theme, int added, int removed)
    {
        var addedStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Success });
        var removedStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Error });
        screen.WriteText(frame.Y, frame.X, new string(' ', ToolPermissionForms.CommandIndent), 0, frame.Right);
        var col = screen.WriteText(frame.Y, frame.X + ToolPermissionForms.CommandIndent, $"+{added}", addedStyle, frame.Right);
        if (removed > 0)
        {
            col = screen.WriteText(frame.Y, col, " ", 0, frame.Right);
            screen.WriteText(frame.Y, col, $"-{removed}", removedStyle, frame.Right);
        }
    }

    /// <summary>Esc 提示行 — Fallback/File 走基准 "Esc to cancel"（零行为变化）；Bash 附 ctrl+e 展开详情提示
    ///（对齐基准 "Esc to cancel · ctrl+e to explain"，S8 键位 ToggleExplanation 数据层已就绪）。</summary>
    private static void RenderEscHint(Screen screen, Rect frame, Theme theme, PermissionFormKind kind)
    {
        if (kind != PermissionFormKind.Bash)
        {
            MessageRenderer.RenderPermissionEscHint(screen, frame, theme);
            return;
        }

        screen.WriteText(frame.Y, frame.X, " Esc to cancel · ctrl+e 展开详情", screen.Styles.GetOrAdd(new TextStyle { Dim = true }), frame.Right);
    }

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };

    /// <summary>无边框参数的单行渲染转发。</summary>
    private static void Row(Screen screen, Rect frame, Theme theme, int row, Action<Screen, Rect, Theme> render) =>
        render(screen, RowFrame(frame, row), theme);
}
