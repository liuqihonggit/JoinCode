namespace JoinCode.Gui.ViewModels.Docking;

/// <summary>
/// Dock 布局工厂 — 构建完整停靠布局：
/// RootDock → ProportionalDock[ToolDock(Left, 6 面板) + Splitter + DocumentDock(Center, 消息区)]。
/// 面板可拖拽排序、拖出浮动窗口、拖回停靠；消息区为中心 Document。
/// </summary>
public sealed class DockFactory : Factory {
    private IRootDock? _root;
    private readonly object _context;

    /// <param name="context">主 ViewModel，设为每个 Tool/Document 的 Context 供 View 绑定</param>
    public DockFactory(object context) => _context = context;

    /// <summary>创建停靠布局：RootDock → ProportionalDock[ToolDock(Left) + Splitter + DocumentDock(Center)]</summary>
    public override IRootDock CreateLayout() {
        var sessions = new PanelTool { Id = "Sessions", Title = "💬 会话列表", Kind = SidePanelKind.Sessions, Context = _context };
        var fileTree = new PanelTool { Id = "FileTree", Title = "📁 目录树", Kind = SidePanelKind.FileTree, Context = _context };
        var goal = new PanelTool { Id = "Goal", Title = "🎯 goal 控制", Kind = SidePanelKind.Goal, Context = _context };
        var interceptor = new PanelTool { Id = "Interceptor", Title = "🛡 拦截器", Kind = SidePanelKind.Interceptor, Context = _context };
        var chatRoom = new PanelTool { Id = "ChatRoom", Title = "👥 聊天室", Kind = SidePanelKind.ChatRoom, Context = _context };
        var settings = new PanelTool { Id = "Settings", Title = "⚙ 设置", Kind = SidePanelKind.Settings, Context = _context };

        var leftToolDock = new ToolDock {
            Id = "LeftTools",
            Title = "面板",
            Alignment = Alignment.Left,
            Proportion = 0.2,
            GripMode = GripMode.Visible,
            ActiveDockable = sessions,
            VisibleDockables = CreateList<IDockable>(sessions, fileTree, goal, interceptor, chatRoom, settings)
        };

        var messageArea = new MessageAreaDocument {
            Id = "MessageArea",
            Title = "消息区",
            Context = _context
        };

        var documentDock = new DocumentDock {
            Id = "Documents",
            Title = "文档",
            IsCollapsable = false,
            CanCreateDocument = false,
            EnableWindowDrag = true,
            ActiveDockable = messageArea,
            VisibleDockables = CreateList<IDockable>(messageArea)
        };

        var mainLayout = new ProportionalDock {
            Id = "Main",
            Title = "主布局",
            Orientation = Dock.Model.Core.Orientation.Horizontal,
            ActiveDockable = documentDock,
            VisibleDockables = CreateList<IDockable>(
                leftToolDock,
                new ProportionalDockSplitter { CanResize = true, ResizePreview = true },
                documentDock)
        };

        var root = CreateRootDock();
        root.Id = "Root";
        root.IsCollapsable = false;
        root.ActiveDockable = mainLayout;
        root.DefaultDockable = mainLayout;
        root.VisibleDockables = CreateList<IDockable>(mainLayout);
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
