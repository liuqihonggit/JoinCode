namespace JoinCode.Tui.Views;

/// <summary>
/// TUI 根视图 — 5层上下结构布局，管理所有子组件。
/// 布局：StatusBar(1行) → ToolBar(1行) → Content(Fill) → Prompt(1行) → Footer(1行)。
/// 用 Pos.Bottom 链式垂直排列（相对定位），Content 用 Dim.Fill(2) 填充中间并底部留2行。
/// 由 TerminalPainter 唯一入口驱动渲染，禁止子组件直接操作 Application。
/// </summary>
public sealed class RootView : View
{
    private readonly TerminalPainter _painter;
    private readonly CommandQueue _queue;
    private readonly View _statusBarArea;
    private readonly View _toolBarArea;
    private readonly View _contentArea;
    private readonly View _promptArea;
    private readonly View _footerArea;

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

        _statusBarArea = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = true,
        };

        _toolBarArea = new View
        {
            X = 0,
            Y = Pos.Bottom(_statusBarArea),
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = true,
        };

        _contentArea = new View
        {
            X = 0,
            Y = Pos.Bottom(_toolBarArea),
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            CanFocus = true,
        };

        _promptArea = new View
        {
            X = 0,
            Y = Pos.Bottom(_contentArea),
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = true,
        };

        _footerArea = new View
        {
            X = 0,
            Y = Pos.Bottom(_promptArea),
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = true,
        };

        Add(_statusBarArea, _toolBarArea, _contentArea, _promptArea, _footerArea);
    }

    /// <summary>状态栏区域（第1层）。</summary>
    public View StatusBarArea => _statusBarArea;

    /// <summary>工具栏区域（第2层）。</summary>
    public View ToolBarArea => _toolBarArea;

    /// <summary>内容区域（第3层，子组件挂载点）。</summary>
    public View ContentArea => _contentArea;

    /// <summary>输入区区域（第4层）。</summary>
    public View PromptArea => _promptArea;

    /// <summary>底部状态栏区域（第5层）。</summary>
    public View FooterArea => _footerArea;

    /// <summary>安装状态栏组件。</summary>
    /// <param name="component">状态栏组件。</param>
    public void SetStatusBar(ITuiComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        _statusBarArea.Add(component.TerminalView);
        _painter.Register(component);
    }

    /// <summary>安装工具栏组件。</summary>
    /// <param name="component">工具栏组件。</param>
    public void SetToolBar(ITuiComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        _toolBarArea.Add(component.TerminalView);
        _painter.Register(component);
    }

    /// <summary>安装输入区组件。</summary>
    /// <param name="component">输入区组件。</param>
    public void SetPrompt(ITuiComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        _promptArea.Add(component.TerminalView);
        _painter.Register(component);
    }

    /// <summary>安装底部状态栏组件。</summary>
    /// <param name="component">底部状态栏组件。</param>
    public void SetFooter(ITuiComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        _footerArea.Add(component.TerminalView);
        _painter.Register(component);
    }

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
