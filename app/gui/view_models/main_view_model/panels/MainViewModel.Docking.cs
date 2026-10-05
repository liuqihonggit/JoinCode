namespace JoinCode.Gui.ViewModels;

/// <summary>
/// Dock.Avalonia 停靠布局 — 管理 DockLayout 属性与工厂初始化。
/// </summary>
public sealed partial class MainViewModel {
    private DockFactory? _dockFactory;

    /// <summary>Dock 停靠布局根 — 绑定到 MainWindow 的 DockControl.Layout</summary>
    public IRootDock? DockLayout {
        get => _dockLayout;
        set => SetProperty(ref _dockLayout, value);
    }
    private IRootDock? _dockLayout;

    /// <summary>初始化 Dock 布局 — 在构造函数末尾调用</summary>
    internal void InitDockLayout() {
        _dockFactory = new DockFactory(this);
        var layout = _dockFactory.CreateLayout();
        _dockFactory.InitLayout(layout);
        DockLayout = layout;
    }

    /// <summary>切换 Dock 面板可见性 — 视图菜单"面板"子菜单调用</summary>
    [RelayCommand]
    private void ToggleDockPanel(SidePanelKind? kind) {
        if (kind is not null)
            _dockFactory?.TogglePanel(kind.Value);
    }
}
