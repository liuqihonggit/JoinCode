namespace JoinCode.Gui.ViewModels.Docking;

/// <summary>
/// Dock 布局工厂 — 构建完整 VSCode 风格停靠布局：
/// RootDock → ProportionalDock(H)[ToolDock(Left,6面板) + Splitter + ProportionalDock(V)[DocumentDock(消息区) + Splitter + ToolDock(Bottom,输入栏+终端)]]。
/// 所有组件可拖拽排序、拖出浮动窗口、拖回停靠、关闭。
/// </summary>
public sealed class DockFactory : Factory {
    private IRootDock? _root;
    private IToolDock? _leftToolDock;
    private readonly object _context;

    /// <param name="context">主 ViewModel，设为每个 Tool/Document 的 Context 供 View 绑定</param>
    public DockFactory(object context) => _context = context;

    /// <summary>创建完整停靠布局</summary>
    public override IRootDock CreateLayout() {
        var sessions = new PanelTool { Id = "Sessions", Title = "💬 会话列表", Kind = SidePanelKind.Sessions, Context = _context, CanClose = true, CanPin = true, CanFloat = true };
        var fileTree = new PanelTool { Id = "FileTree", Title = "📁 目录树", Kind = SidePanelKind.FileTree, Context = _context, CanClose = true, CanPin = true, CanFloat = true };
        var goal = new PanelTool { Id = "Goal", Title = "🎯 goal 控制", Kind = SidePanelKind.Goal, Context = _context, CanClose = true, CanPin = true, CanFloat = true };
        var interceptor = new PanelTool { Id = "Interceptor", Title = "🛡 拦截器", Kind = SidePanelKind.Interceptor, Context = _context, CanClose = true, CanPin = true, CanFloat = true };
        var chatRoom = new PanelTool { Id = "ChatRoom", Title = "👥 聊天室", Kind = SidePanelKind.ChatRoom, Context = _context, CanClose = true, CanPin = true, CanFloat = true };
        var settings = new PanelTool { Id = "Settings", Title = "⚙ 设置", Kind = SidePanelKind.Settings, Context = _context, CanClose = true, CanPin = true, CanFloat = true };

        var leftToolDock = new ToolDock {
            Id = "LeftTools",
            Title = "面板",
            Alignment = Alignment.Left,
            Proportion = 0.22,
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

        var inputBar = new InputBarTool {
            Id = "InputBar",
            Title = "⌨ 输入栏",
            Context = _context,
            CanClose = true,
            CanPin = true,
            CanFloat = true
        };

        var terminal = new TerminalTool {
            Id = "Terminal",
            Title = "🖥 终端",
            Context = _context,
            CanClose = true,
            CanPin = true,
            CanFloat = true
        };

        var bottomToolDock = new ToolDock {
            Id = "BottomTools",
            Title = "底部面板",
            Alignment = Alignment.Bottom,
            Proportion = 0.35,
            GripMode = GripMode.Visible,
            ActiveDockable = inputBar,
            VisibleDockables = CreateList<IDockable>(inputBar, terminal)
        };

        var centerLayout = new ProportionalDock {
            Id = "Center",
            Title = "中心区",
            Orientation = Dock.Model.Core.Orientation.Vertical,
            ActiveDockable = documentDock,
            VisibleDockables = CreateList<IDockable>(
                documentDock,
                new ProportionalDockSplitter { CanResize = true, ResizePreview = true },
                bottomToolDock)
        };

        var mainLayout = new ProportionalDock {
            Id = "Main",
            Title = "主布局",
            Orientation = Dock.Model.Core.Orientation.Horizontal,
            ActiveDockable = centerLayout,
            VisibleDockables = CreateList<IDockable>(
                leftToolDock,
                new ProportionalDockSplitter { CanResize = true, ResizePreview = true },
                centerLayout)
        };

        var root = CreateRootDock();
        root.Id = "Root";
        root.IsCollapsable = false;
        root.ActiveDockable = mainLayout;
        root.DefaultDockable = mainLayout;
        root.VisibleDockables = CreateList<IDockable>(mainLayout);
        root.PinnedDock = null;

        _root = root;
        _leftToolDock = leftToolDock;
        return root;
    }

    /// <summary>初始化布局 — 注册 HostWindowLocator，委托基类，默认收拢左侧面板</summary>
    public override void InitLayout(IDockable layout) {
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>> {
            [nameof(IDockWindow)] = () => new Dock.Avalonia.Controls.HostWindow()
        };
        base.InitLayout(layout);
        DefaultPinLeftPanels();
    }

    /// <summary>把左侧面板默认 pin（收拢为侧边图标条），鼠标悬停才展开</summary>
    private void DefaultPinLeftPanels() {
        if (_leftToolDock?.VisibleDockables is { } dockables) {
            foreach (var dockable in dockables.ToList()) {
                PinDockable(dockable);
            }
        }
    }
}
