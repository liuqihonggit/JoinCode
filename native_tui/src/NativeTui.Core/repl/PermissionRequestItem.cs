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
    /// + 描述(空则 0) + 形态区行数（Bash：命令折行 + 警告 0/1 / File：路径 1 + patch 0/1）
    /// + 反馈输入行（P3 — Tab 展开时 1 行）+ 触发原因区（P5 — 规则内容非空时 2 行）。</summary>
    public int MeasureHeight(int width) =>
        12 + DescriptionRows + FormRows(width) + FeedbackRows + ReasonRows;

    /// <summary>触发原因区行数（P5 — 规则内容 + /permissions 引导两行；无规则内容折叠为 0）。</summary>
    private int ReasonRows => string.IsNullOrEmpty(Coordinator.Request?.RuleContent) ? 0 : 2;

    /// <summary>反馈输入行数（P3 — Tab 展开输入框时占 1 行）。</summary>
    private int FeedbackRows => Coordinator.FeedbackInputActive ? 1 : 0;

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
        RenderReasonRegion(screen, frame, ref row, request);

        row++;
        MessageRenderer.RenderPermissionQuestion(screen, RowFrame(frame, row++), theme);
        for (var i = 0; i < PermissionCoordinator.OptionCount; i++)
        {
            MessageRenderer.RenderPermissionOption(screen, RowFrame(frame, row++), theme, i, OptionLabels[i], i == Coordinator.FocusIndex);
            if (i == Coordinator.FocusIndex && Coordinator.FeedbackInputActive)
            {
                RenderFeedbackInput(screen, RowFrame(frame, row++), theme);
            }
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
            if (form.FilePath is { } filePath)
            {
                RenderIndentedLine(screen, RowFrame(frame, row++), 0, filePath);
            }
            if (form.HasPatchSummary)
            {
                RenderPatchSummary(screen, RowFrame(frame, row++), theme, form.PatchAdded, form.PatchRemoved);
            }
        }
    }

    /// <summary>
    /// 触发原因区渲染（TASK069-P5）— 对齐基准 <c>components/permissions/PermissionRuleExplanation.tsx</c>：
    /// 该组件是「原因句 + dim 配置引导」两行的竖排 Box（:109 <c>marginBottom={1}</c> 即后置一空行，
    /// 由本项既有的 paddingY 空行承担）。<c>rule</c> 分支（:39-43）措辞为
    /// <c>Permission rule &lt;规则&gt; requires confirmation for this &lt;toolType&gt;.</c> +
    /// <c>/permissions to update rules</c>；无 reason 时整区 <c>return null</c>（:86-88）——
    /// 与本实现的「空规则内容折叠为 0 行」同义。
    /// <para>**未移植分支（本仓库无对应来源，不编造）**：<c>hook</c>（:44-52，引导 <c>/hooks to update</c>）、
    /// <c>workingDir</c>（:59-63）、<c>safetyCheck</c>/<c>other</c>（:53-58，无引导）、
    /// <c>classifier</c>（:25-37，BASH_CLASSIFIER 特性门内）。C# 权限检查目前只有 WebFetch 会给出
    /// 结构化规则内容（<c>domain:hostname</c>），其余待确认没有可指名的规则。</para>
    /// <para>引导行恒显示：基准只在 <c>rule.source === 'policySettings'</c> 时隐藏（企业策略改不动），
    /// 而本仓库权限规则只有本地 settings 一个来源。</para>
    /// </summary>
    private static void RenderReasonRegion(Screen screen, Rect frame, ref int row, PermissionRequest request)
    {
        if (string.IsNullOrEmpty(request.RuleContent))
        {
            return;
        }

        RenderIndentedLine(screen, RowFrame(frame, row++), 0, $"触发原因: {request.ToolName}({request.RuleContent})");
        RenderIndentedLine(screen, RowFrame(frame, row++), screen.Styles.GetOrAdd(new TextStyle { Dim = true }), "/permissions 更新规则");
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
    ///（对齐基准 "Esc to cancel · ctrl+e to explain"，S8 键位 ToggleExplanation 数据层已就绪）；
    /// 焦点档支持反馈时附 Tab 说明提示（P3 — 对齐基准 "Esc to cancel · Tab to amend"）。</summary>
    private void RenderEscHint(Screen screen, Rect frame, Theme theme, PermissionFormKind kind)
    {
        var text = kind == PermissionFormKind.Bash ? " Esc to cancel · ctrl+e 展开详情" : " Esc to cancel";
        if (Coordinator.FocusSupportsFeedback)
        {
            text += Coordinator.FeedbackInputActive
                ? $" · Tab 收起说明（{Coordinator.Feedback.Length} 字）"
                : " · Tab 修改说明";
        }

        screen.WriteText(frame.Y, frame.X, text, screen.Styles.GetOrAdd(new TextStyle { Dim = true }), frame.Right);
    }

    /// <summary>反馈输入行渲染（P3）— 与选项标签同列（5 列缩进）：有内容走默认样式 + 尾随光标「_」，
    /// 空内容走 dim 占位文案（按焦点档区分 accept/reject 两套措辞）。</summary>
    private void RenderFeedbackInput(Screen screen, Rect frame, Theme theme)
    {
        const int labelColumn = 5;
        if (Coordinator.Feedback.Length == 0)
        {
            screen.WriteText(frame.Y, frame.X, $"{new string(' ', labelColumn)}{Coordinator.FeedbackPlaceholder}", screen.Styles.GetOrAdd(new TextStyle { Dim = true }), frame.Right);
            return;
        }

        var col = screen.WriteText(frame.Y, frame.X, new string(' ', labelColumn), 0, frame.Right);
        col = screen.WriteText(frame.Y, col, Coordinator.Feedback, 0, frame.Right);
        screen.WriteText(frame.Y, col, "_", screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Suggestion }), frame.Right);
    }

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };

    /// <summary>无边框参数的单行渲染转发。</summary>
    private static void Row(Screen screen, Rect frame, Theme theme, int row, Action<Screen, Rect, Theme> render) =>
        render(screen, RowFrame(frame, row), theme);
}
