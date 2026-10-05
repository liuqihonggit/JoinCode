namespace JoinCode.Gui.ViewModels.Docking;

/// <summary>
/// Dock 布局工厂 — 构建完整 VSCode 风格停靠布局：
/// RootDock → ProportionalDock(H)[ToolDock(Left,6面板) + Splitter + ProportionalDock(V)[DocumentDock(消息区) + Splitter + ToolDock(Bottom,输入栏+终端)]]。
/// 所有组件可拖拽排序、拖出浮动窗口、拖回停靠、关闭。
/// </summary>
public sealed class DockFactory : Factory {
    private IRootDock? _root;
    private IToolDock? _leftToolDock;
    private IToolDock? _bottomLeftDock;
    private IDocumentDock? _documentDock;
    private readonly object _context;

    /// <summary>所有左侧面板引用（即使关闭后也保留，供视图菜单重新打开）</summary>
    private readonly Dictionary<SidePanelKind, PanelTool> _allPanels = new();

    /// <param name="context">主 ViewModel，设为每个 Tool/Document 的 Context 供 View 绑定</param>
    public DockFactory(object context) => _context = context;

    /// <summary>创建完整停靠布局</summary>
    public override IRootDock CreateLayout() {
        var sessions = new PanelTool { Id = "Sessions", Title = "💬 会话列表", Kind = SidePanelKind.Sessions, Context = _context, CanClose = true, CanPin = true, CanFloat = true, MinWidth = 300 };
        var fileTree = new PanelTool { Id = "FileTree", Title = "📁 目录树", Kind = SidePanelKind.FileTree, Context = _context, CanClose = true, CanPin = true, CanFloat = true, MinWidth = 300 };
        var goal = new PanelTool { Id = "Goal", Title = "🎯 goal 控制", Kind = SidePanelKind.Goal, Context = _context, CanClose = true, CanPin = true, CanFloat = true, MinWidth = 300 };
        var interceptor = new PanelTool { Id = "Interceptor", Title = "🛡 拦截器", Kind = SidePanelKind.Interceptor, Context = _context, CanClose = true, CanPin = true, CanFloat = true, MinWidth = 300 };
        var chatRoom = new PanelTool { Id = "ChatRoom", Title = "👥 聊天室", Kind = SidePanelKind.ChatRoom, Context = _context, CanClose = true, CanPin = true, CanFloat = true, MinWidth = 300 };
        var settings = new PanelTool { Id = "Settings", Title = "⚙ 设置", Kind = SidePanelKind.Settings, Context = _context, CanClose = true, CanPin = true, CanFloat = true, MinWidth = 300 };

        _allPanels[SidePanelKind.Sessions] = sessions;
        _allPanels[SidePanelKind.FileTree] = fileTree;
        _allPanels[SidePanelKind.Goal] = goal;
        _allPanels[SidePanelKind.Interceptor] = interceptor;
        _allPanels[SidePanelKind.ChatRoom] = chatRoom;
        _allPanels[SidePanelKind.Settings] = settings;

        var leftToolDock = new ToolDock {
            Id = "LeftTools",
            Title = "面板",
            Alignment = Alignment.Left,
            Proportion = 0.22,
            GripMode = GripMode.Visible,
            ActiveDockable = sessions,
            VisibleDockables = CreateList<IDockable>(sessions, fileTree, goal, interceptor, chatRoom, settings)
        };

        var messageDocs = CreateMessageDocuments();
        var documentDock = new DocumentDock {
            Id = "Documents",
            Title = "文档",
            IsCollapsable = false,
            CanCreateDocument = false,
            EnableWindowDrag = true,
            ActiveDockable = messageDocs[0],
            VisibleDockables = messageDocs
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

        var bottomLeftDock = new ToolDock {
            Id = "BottomLeftTools",
            Title = "终端/日志",
            Alignment = Alignment.Left,
            Proportion = 0.5,
            GripMode = GripMode.Visible,
            ActiveDockable = terminal,
            VisibleDockables = CreateList<IDockable>(terminal)
        };

        var bottomRightDock = new ToolDock {
            Id = "BottomRightTools",
            Title = "输入区",
            Alignment = Alignment.Right,
            Proportion = 0.5,
            GripMode = GripMode.Visible,
            ActiveDockable = inputBar,
            VisibleDockables = CreateList<IDockable>(inputBar)
        };

        var bottomLayout = new ProportionalDock {
            Id = "Bottom",
            Title = "底部区",
            Orientation = Dock.Model.Core.Orientation.Horizontal,
            ActiveDockable = bottomLeftDock,
            VisibleDockables = CreateList<IDockable>(
                bottomLeftDock,
                new ProportionalDockSplitter { CanResize = true, ResizePreview = true },
                bottomRightDock)
        };

        var centerLayout = new ProportionalDock {
            Id = "Center",
            Title = "中心区",
            Orientation = Dock.Model.Core.Orientation.Vertical,
            ActiveDockable = documentDock,
            VisibleDockables = CreateList<IDockable>(
                documentDock,
                new ProportionalDockSplitter { CanResize = true, ResizePreview = true },
                bottomLayout)
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
        _bottomLeftDock = bottomLeftDock;
        _documentDock = documentDock;
        return root;
    }

    /// <summary>为每个会话创建一个消息区 Document，无会话时创建默认</summary>
    private List<IDockable> CreateMessageDocuments() {
        var docs = new List<IDockable>();
        if (_context is MainViewModel vm && vm.Sessions.Count > 0) {
            foreach (var session in vm.Sessions) {
                docs.Add(new MessageAreaDocument {
                    Id = $"Msg_{session.Id}",
                    Title = session.Title,
                    Context = _context
                });
            }
        } else {
            docs.Add(new MessageAreaDocument { Id = "MessageArea", Title = "消息区", Context = _context });
        }
        return docs;
    }

    /// <summary>初始化布局 — 注册 HostWindowLocator，委托基类，默认收拢左侧面板，监听会话变化</summary>
    public override void InitLayout(IDockable layout) {
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>> {
            [nameof(IDockWindow)] = () => new Dock.Avalonia.Controls.HostWindow()
        };
        base.InitLayout(layout);
        DefaultPinLeftPanels();
        WatchSessionChanges();
    }

    /// <summary>监听 Sessions 集合变化，动态增删消息区 Document</summary>
    private void WatchSessionChanges() {
        if (_context is not MainViewModel vm) return;
        if (_documentDock is not { } dock) return;
        vm.Sessions.CollectionChanged += (_, e) => OnSessionsChanged(dock, e);
    }

    /// <summary>Sessions 集合变化处理 — 扁平化避免深嵌套</summary>
    private void OnSessionsChanged(IDocumentDock dock, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
        if (e.NewItems is not null) AddSessionDocuments(dock, e.NewItems);
        if (e.OldItems is not null) RemoveSessionDocuments(dock, e.OldItems);
    }

    /// <summary>新增会话 → 创建对应 Document 并激活</summary>
    private void AddSessionDocuments(IDocumentDock dock, System.Collections.IList newItems) {
        dock.VisibleDockables ??= CreateList<IDockable>();
        foreach (SessionItem s in newItems) {
            var doc = new MessageAreaDocument { Id = $"Msg_{s.Id}", Title = s.Title, Context = _context };
            dock.VisibleDockables.Add(doc);
            dock.ActiveDockable = doc;
        }
    }

    /// <summary>删除会话 → 移除对应 Document</summary>
    private void RemoveSessionDocuments(IDocumentDock dock, System.Collections.IList oldItems) {
        var list = dock.VisibleDockables;
        if (list is null) return;
        foreach (SessionItem s in oldItems) {
            for (var i = list.Count - 1; i >= 0; i--) {
                if (list[i].Id == $"Msg_{s.Id}") list.RemoveAt(i);
            }
        }
    }

    /// <summary>激活指定会话的 Document — 供会话选中时调用</summary>
    public void ActivateSession(string sessionId) {
        if (_documentDock?.VisibleDockables is not { } list) return;
        var doc = list.FirstOrDefault(d => d.Id == $"Msg_{sessionId}");
        if (doc is not null) _documentDock.ActiveDockable = doc;
    }

    /// <summary>把左侧面板默认 pin（收拢为侧边图标条），鼠标悬停才展开</summary>
    private void DefaultPinLeftPanels() {
        if (_leftToolDock?.VisibleDockables is { } dockables) {
            foreach (var dockable in dockables.ToList()) {
                PinDockable(dockable);
                dockable.PinnedBounds = new DockRect(0, 0, 300, 600);
            }
        }
    }

    /// <summary>切换面板可见性 — 可见则关闭，不可见则重新显示</summary>
    public void TogglePanel(SidePanelKind kind) {
        if (!_allPanels.TryGetValue(kind, out var panel)) return;
        if (_leftToolDock is null) return;
        var list = _leftToolDock.VisibleDockables;
        if (list is null) return;
        if (list.Contains(panel)) {
            CloseDockable(panel);
        } else {
            AddDockable(_leftToolDock, panel);
            _leftToolDock.ActiveDockable = panel;
        }
    }

    /// <summary>面板是否当前可见</summary>
    public bool IsPanelVisible(SidePanelKind kind) {
        if (!_allPanels.TryGetValue(kind, out var panel)) return false;
        return _leftToolDock?.VisibleDockables?.Contains(panel) ?? false;
    }

    /// <summary>切换底部面板可见性 — 显隐终端/日志面板</summary>
    public void ToggleBottomPanel() {
        if (_bottomLeftDock is null) return;
        var list = _bottomLeftDock.VisibleDockables;
        if (list is null || list.Count == 0) return;
        var dockable = list[0];
        if (_bottomLeftDock.ActiveDockable is not null) {
            CloseDockable(dockable);
        } else {
            AddDockable(_bottomLeftDock, dockable);
            _bottomLeftDock.ActiveDockable = dockable;
        }
    }

    /// <summary>关闭当前激活的左侧 Dock 面板</summary>
    public void CloseActiveDockPanel() {
        if (_leftToolDock?.ActiveDockable is { } active) {
            CloseDockable(active);
        }
    }
}
