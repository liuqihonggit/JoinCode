namespace JoinCode.Tui.Rendering;

/// <summary>
/// TUI 组件接口 — 所有 TUI 组件实现此接口，由 RootView 统一管理。
/// 组件通过 TerminalPainter 投递渲染，禁止直接操作 Terminal.Gui Application。
/// </summary>
public interface ITuiComponent
{
    /// <summary>Terminal.Gui View 根节点。</summary>
    View TerminalView { get; }

    /// <summary>队列状态变化通知（驱动"投递中"组件等）。</summary>
    void OnQueueChanged(QueueSnapshot snapshot);

    /// <summary>终端尺寸变化通知。</summary>
    void OnResize(int cols, int rows);
}
