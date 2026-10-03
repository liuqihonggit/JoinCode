namespace JoinCode.Gui.ViewModels.Docking;

/// <summary>
/// Dock 布局工厂 — 构建 6 面板可停靠布局：
/// RootDock → [ToolDock(Left, 6 面板)]。
/// 面板可拖拽排序、拖出浮动窗口、拖回停靠。
/// </summary>
public sealed class DockFactory : Factory {
    private IRootDock? _root;

    /// <summary>创建停靠布局：RootDock → ToolDock(Left, 6 面板)</summary>
    public override IRootDock CreateLayout() {
        var sessions = new PanelTool { Id = "Sessions", Title = "会话列表", Kind = SidePanelKind.Sessions };
        var fileTree = new PanelTool { Id = "FileTree", Title = "目录树", Kind = SidePanelKind.FileTree };
        var goal = new PanelTool { Id = "Goal", Title = "goal 控制", Kind = SidePanelKind.Goal };
        var interceptor = new PanelTool { Id = "Interceptor", Title = "AI 工具拦截器", Kind = SidePanelKind.Interceptor };
        var chatRoom = new PanelTool { Id = "ChatRoom", Title = "聊天室", Kind = SidePanelKind.ChatRoom };
        var settings = new PanelTool { Id = "Settings", Title = "设置", Kind = SidePanelKind.Settings };

        var toolDock = new ToolDock {
            Id = "LeftTools",
            Title = "面板",
            Alignment = Alignment.Left,
            GripMode = GripMode.Visible,
            ActiveDockable = sessions,
            VisibleDockables = CreateList<IDockable>(sessions, fileTree, goal, interceptor, chatRoom, settings)
        };

        var root = CreateRootDock();
        root.Id = "Root";
        root.IsCollapsable = false;
        root.ActiveDockable = toolDock;
        root.DefaultDockable = toolDock;
        root.VisibleDockables = CreateList<IDockable>(toolDock);
        root.PinnedDock = null;

        _root = root;
        return root;
    }

    /// <summary>初始化布局 — 注册 HostWindowLocator 并委托基类</summary>
    public override void InitLayout(IDockable layout) {
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>> {
            [nameof(IDockWindow)] = () => new Dock.Avalonia.Controls.HostWindow()
        };
        base.InitLayout(layout);
    }
}
