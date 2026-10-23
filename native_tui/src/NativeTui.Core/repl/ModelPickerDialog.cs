namespace NativeTui.Core.Repl;

/// <summary>
/// 模型选择对话框消息项（M7-6，全部自研）— 对齐基准 ModelPicker 渲染结构
/// （标题 "Select model" + 副标题 "Switch between Claude models..." + Select 单列列表 +
/// "and N more…" 溢出 + effort 独立区块[TASK066 D5 — 基准 ModelPicker.tsx:326-336 列表区块后] +
/// Byline 快捷键行）：
/// margin 1 + 顶边框（permission 色全宽，仅 top 无角，同权限对话框）+ 标题（1 缩进 bold permission）
/// + 副标题（1 缩进 dim）+ 空行 + 每可见项 2 行（名称行：❯/空格指示 + "N. " dim + 名称 +
/// 当前项尾 ✓ success；描述行：5 空格缩进 dim，空描述折叠）+ 溢出行（1 缩进 dim "and N more…"）
/// + effort 行（启用时：焦点项支持 → "◉ Max effort (default) ← → to adjust" dim；不支持 →
/// "○ Effort not supported for {模型名}" dim）+ 空行 + 快捷键行（1 缩进 dim "Enter confirm · Esc exit"）。
/// 状态源 = ModelPickerCoordinator（焦点/窗口/挂起/effort），Begin 时插入 Messages，确认/取消时移除。
/// title/subtitle 构造参数化（默认模型文案）— vendor 选择器复用同条渲染链（effort 区经
/// Coordinator.EffortEnabled 关闭 — vendor Begin 不注入 effort 配置）。
/// </summary>
public sealed class ModelPickerDialog(ModelPickerCoordinator coordinator, string title = "Select model", string subtitle = "Switch between models. Applies to this session and future sessions.") : IScrollItem
{
    /// <summary>固定快捷键行（基准 Byline "Enter confirm · Esc exit"）。</summary>
    private const string Shortcuts = "Enter confirm · Esc exit";

    /// <summary>溢出行前缀+后缀（基准 "and {hiddenCount} more…"）。</summary>
    private const string OverflowPrefix = " and ";
    private const string OverflowSuffix = " more…";

    /// <summary>协调器（焦点/窗口/挂起状态的唯一来源）。</summary>
    public ModelPickerCoordinator Coordinator { get; } = coordinator;

    /// <summary>内容高度 = margin 1 + 边框 1 + 标题 1 + 副标题 1 + 空行 1 +
    /// 可见项（名称 1 + 描述 0/1）+ 溢出 0/1 + effort 行 0/1（启用时 — TASK066 D5）+ 空行 1 + 快捷键 1。</summary>
    public int MeasureHeight(int width) =>
        6 + VisibleItemHeights() + (Coordinator.HiddenCount > 0 ? 1 : 0) + (Coordinator.EffortEnabled ? 1 : 0) + 1;

    /// <summary>渲染：按固定行序绘制（状态由 Coordinator 投影 — 挂起被清除时只画空 margin）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        if (!Coordinator.IsPending)
        {
            return;
        }

        var row = frame.Y + 1;
        MessageRenderer.RenderPermissionBorder(screen, RowFrame(frame, row++), theme);
        MessageRenderer.RenderModelTitle(screen, RowFrame(frame, row++), theme, title);
        MessageRenderer.RenderModelSubtitle(screen, RowFrame(frame, row++), theme, subtitle);
        row++;

        for (var i = 0; i < Coordinator.VisibleCount; i++)
        {
            var itemIndex = Coordinator.WindowStart + i;
            var item = Coordinator.Items[itemIndex];
            MessageRenderer.RenderModelListRow(screen, RowFrame(frame, row++), theme, itemIndex, item.DisplayName, item.IsCurrent, itemIndex == Coordinator.FocusIndex);
            if (!string.IsNullOrEmpty(item.Description))
            {
                MessageRenderer.RenderModelDescription(screen, RowFrame(frame, row++), theme, item.Description);
            }
        }

        if (Coordinator.HiddenCount > 0)
        {
            MessageRenderer.RenderModelOverflow(screen, RowFrame(frame, row++), theme, Coordinator.HiddenCount);
        }

        // effort 区（TASK066 D5）— 列表区块后独立区块（基准 ModelPicker.tsx:326-336 t24：
        // marginBottom 的列表 → effort 行 → 后续区块），焦点项支持 → 档位行，否则 not supported 行。
        if (Coordinator.EffortEnabled)
        {
            if (Coordinator.Focused is { SupportsEffort: true } focused)
            {
                MessageRenderer.RenderModelEffortRow(screen, RowFrame(frame, row++), theme, Coordinator.DisplayEffort, Coordinator.DisplayEffort == focused.DefaultEffort);
            }
            else
            {
                MessageRenderer.RenderModelEffortUnsupportedRow(screen, RowFrame(frame, row++), theme, Coordinator.Focused?.DisplayName);
            }
        }

        row++;
        MessageRenderer.RenderModelShortcuts(screen, RowFrame(frame, row), theme, Shortcuts);
    }

    /// <summary>可见项总高度（名称行 + 非空描述行）。</summary>
    private int VisibleItemHeights()
    {
        var height = 0;
        for (var i = 0; i < Coordinator.VisibleCount; i++)
        {
            height += string.IsNullOrEmpty(Coordinator.Items[Coordinator.WindowStart + i].Description) ? 1 : 2;
        }

        return height;
    }

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };
}
