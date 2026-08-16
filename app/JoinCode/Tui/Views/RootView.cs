namespace JoinCode.Tui.Views;

/// <summary>
/// TUI 根视图 — 管理所有子组件，监听终端 resize 事件并分发。
/// 由 TerminalPainter 唯一入口驱动渲染，禁止子组件直接操作 Application。
/// Terminal.Gui v2 通过 Dim.Fill()/Pos.Center() 声明式布局自动适应终端 resize。
/// </summary>
public sealed class RootView : View
{
    private readonly TerminalPainter _painter;
    private readonly CommandQueue _queue;
    private readonly View _contentArea;

    /// <summary>
    /// 创建 RootView。
    /// </summary>
    /// <param name="painter">终端绘制入口。</param>
    /// <param name="queue">命令队列（驱动"投递中"组件）。</param>
    public RootView(TerminalPainter painter, CommandQueue queue)
    {
        _painter = painter ?? throw new ArgumentNullException(nameof(painter));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));

        Width = Dim.Fill();
        Height = Dim.Fill();

        _contentArea = new View
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        Add(_contentArea);
    }

    /// <summary>内容区域（子组件挂载点）。</summary>
    public View ContentArea => _contentArea;

    /// <summary>添加子组件到内容区域并注册到 TerminalPainter。</summary>
    /// <param name="component">TUI 组件。</param>
    public void AddComponent(ITuiComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        _contentArea.Add(component.TerminalView);
        _painter.Register(component);
    }

    /// <summary>移除子组件。</summary>
    /// <param name="component">TUI 组件。</param>
    public void RemoveComponent(ITuiComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        _contentArea.Remove(component.TerminalView);
        _painter.Unregister(component);
    }
}
