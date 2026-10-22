namespace NativeTui.Core.Repl;

/// <summary>
/// 导出目标（Export 对话框确认结果 — 宿主分派 IO 的唯一依据）。
/// 序数与选项索引对齐（0 = 剪贴板 / 1 = 文件 — ExportDialog.SelectedIndex 直映）。
/// </summary>
public enum ExportTarget
{
    /// <summary>复制到剪贴板（宿主执行剪贴板写入 — 组件零 IO）。</summary>
    Clipboard,

    /// <summary>保存为 Markdown 文件（宿主执行文件写入 — 组件零 IO）。</summary>
    File,
}

/// <summary>
/// 导出对话对话框（Export 对话框域）— 对齐基准 components/ExportDialog.tsx 选项选择阶段
/// （Dialog + Select 两选项 clipboard/file + Esc 取消；基准的文件名输入子屏为后续增强 —
/// 本项目开心路径暂不实现，宿主以默认文件名直写，见未决问题）。
/// 状态机即抛形态（对比 PermissionCoordinator 的 TCS 桥：/export 无引擎阻塞等待方，
/// 宿主同步拦截即可）：SelectedIndex 焦点（初始 0 — 基准 Select 首项焦点）+
/// Closed/Confirmed 终态（Enter 确认 / Esc 取消 / 1-2 数字直选一步确认 —
/// 数字直选语义对齐权限对话框数字选档，↑↓ 环绕对齐 Select 焦点语义）。
/// 纯 UI 零 IO：确认动作经 Closed + Confirmed + SelectedTarget 暴露，
/// 剪贴板写入/文件写入留宿主接线（/export 命令拦截在 ReplShell — 未决问题）。
/// 渲染骨架对齐权限/模型对话框先例：margin 1 + 顶边框 permission 色全宽 +
/// 标题 "导出对话"（bold permission）+ 副标题 dim + 空行 + 选项行 ×2
/// （焦点 ❯ suggestion + "N. " dim 前缀 + label — 复用 MessageRenderer.RenderPermissionOption）+
/// 空行 + footer 快捷键提示 dim（复用 RenderModelShortcuts）。
/// </summary>
public sealed class ExportDialog : IScrollItem
{
    /// <summary>选项档数（剪贴板 / 文件）。</summary>
    public const int OptionCount = 2;

    /// <summary>标题文案（基准 "Export Conversation" 中文化）。</summary>
    private const string TitleText = "导出对话";

    /// <summary>副标题文案（基准 "Select export method:" 中文化）。</summary>
    private const string SubtitleText = "选择导出方式：";

    /// <summary>剪贴板选项文案（ExportTarget.Clipboard 序数 0）。</summary>
    private const string ClipboardLabel = "复制到剪贴板";

    /// <summary>文件选项文案（ExportTarget.File 序数 1）。</summary>
    private const string FileLabel = "保存为 Markdown 文件";

    /// <summary>footer 快捷键提示（基准 Byline Esc cancel 中文化 + 本项目键位面 — Middot 分隔对齐 Figures）。</summary>
    private const string ShortcutsText =
        $"↑↓ 选择{Figures.MiddotSeparator}1/2 直选{Figures.MiddotSeparator}enter 确认{Figures.MiddotSeparator}esc 取消";

    /// <summary>选项文案表（序数对齐 ExportTarget — 0 剪贴板 / 1 文件，单一数据源）。</summary>
    private static readonly string[] Labels = [ClipboardLabel, FileLabel];

    /// <summary>选中选项索引（0 = 剪贴板 / 1 = 文件；初始 0，↑↓ 环绕移动）。</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>是否已关闭（Enter 确认 / Esc 取消 / 数字直选均关闭 — 宿主读取终态后移除本项）。</summary>
    public bool Closed { get; private set; }

    /// <summary>是否经确认关闭（Enter / 数字直选 = true；Esc 取消 = false — 宿主据此分派 IO 或静默收尾）。</summary>
    public bool Confirmed { get; private set; }

    /// <summary>导出目标（序数直映 SelectedIndex — 宿主在 Closed 且 Confirmed 时消费执行写入）。</summary>
    public ExportTarget SelectedTarget => (ExportTarget)SelectedIndex;

    /// <summary>内容高度 = margin 1 + 边框 1 + 标题 1 + 副标题 1 + 空行 1 + 选项 2 + 空行 1 + footer 1 = 9。</summary>
    public int MeasureHeight(int width) => 9;

    /// <summary>渲染：固定行序绘制（Closed 后宿主应移除本项 — 防御性只画空 margin，对齐 ModelPickerDialog 挂起清除先例）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        if (Closed)
        {
            return;
        }

        var row = frame.Y + 1;
        MessageRenderer.RenderPermissionBorder(screen, RowFrame(frame, row++), theme);
        MessageRenderer.RenderModelTitle(screen, RowFrame(frame, row++), theme, TitleText);
        MessageRenderer.RenderModelSubtitle(screen, RowFrame(frame, row++), theme, SubtitleText);
        row++;
        row = RenderOptions(screen, frame, theme, row);
        row++;
        MessageRenderer.RenderModelShortcuts(screen, RowFrame(frame, row), theme, ShortcutsText);
    }

    /// <summary>键位路由 — ↑↓ 焦点环绕 / 1-2 数字直选一步确认 / Enter 确认 / Esc 取消；
    /// 消费返回 true，未消费返回 false（宿主继续分派其他处理器 — 对齐 TranscriptController.TryHandleKey 契约）；
    /// Closed 后恒 false（宿主应已移除本项 — 防御性 no-op）。</summary>
    /// <param name="key">按键事件（ReplShell 模态改道转发）。</param>
    /// <returns>是否消费（true = 已处理；false = 未消费透传宿主）。</returns>
    public bool TryHandleKey(KeyPress key)
    {
        if (Closed)
        {
            return false;
        }

        return key.Kind switch
        {
            KeyKind.UpArrow => MoveSelection(-1),
            KeyKind.DownArrow => MoveSelection(1),
            KeyKind.Return => ConfirmSelection(),
            KeyKind.Escape => CancelSelection(),
            KeyKind.Char when key.Input is "1" => SelectDirect(0),
            KeyKind.Char when key.Input is "2" => SelectDirect(1),
            _ => false,
        };
    }

    /// <summary>选项行组渲染（逐档调用 RenderPermissionOption — 焦点档 ❯ suggestion 高亮）。</summary>
    private int RenderOptions(Screen screen, Rect frame, Theme theme, int row)
    {
        for (var i = 0; i < OptionCount; i++)
        {
            MessageRenderer.RenderPermissionOption(screen, RowFrame(frame, row++), theme, i, Labels[i], i == SelectedIndex);
        }

        return row;
    }

    /// <summary>焦点环绕移动（Select 语义 — 对齐权限对话框环绕，区别于模型选择器 clamp 列表语义）。</summary>
    private bool MoveSelection(int delta)
    {
        SelectedIndex = ((SelectedIndex + delta) % OptionCount + OptionCount) % OptionCount;
        return true;
    }

    /// <summary>确认当前焦点选项（Enter — Closed + Confirmed，SelectedTarget 随焦点档）。</summary>
    private bool ConfirmSelection()
    {
        Closed = true;
        Confirmed = true;
        return true;
    }

    /// <summary>取消（Esc — Closed 未确认，Confirmed 保持 false — 宿主静默收尾不执行 IO）。</summary>
    private bool CancelSelection()
    {
        Closed = true;
        return true;
    }

    /// <summary>数字直选（1/2 — 焦点定位该档并一步确认，对齐权限对话框数字选档语义）。</summary>
    private bool SelectDirect(int index)
    {
        SelectedIndex = index;
        Closed = true;
        Confirmed = true;
        return true;
    }

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };
}
