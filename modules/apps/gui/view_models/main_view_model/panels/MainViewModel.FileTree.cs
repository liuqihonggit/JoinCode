namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 侧边面板类型 — Activity Bar 图标对应的面板。
/// </summary>
public enum SidePanelKind {
    /// <summary>无面板展开(全部折叠)</summary>
    None,
    /// <summary>会话列表面板</summary>
    Sessions,
    /// <summary>目录树面板</summary>
    FileTree,
    /// <summary>设置面板</summary>
    Settings,
    /// <summary>goal 控制面板</summary>
    Goal,
    /// <summary>AI 工具拦截器面板</summary>
    Interceptor,
    /// <summary>聊天室面板</summary>
    ChatRoom
}

/// <summary>
/// MainViewModel 侧边面板+目录树 partial — VSCode Activity Bar + Side Bar 模式。
/// 最左侧一条图标列(Activity Bar,48px),点击图标在右侧展开对应面板(Side Bar,236px)。
/// 再点同一个图标收起面板。同一时刻最多展开一个面板。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>展开态面板默认宽度</summary>
    private const double SidePanelDefaultWidth = 236;

    /// <summary>磁吸折叠阈值 — 拖拽宽度低于此值自动折叠</summary>
    private const double SidePanelSnapCollapseThreshold = 80;

    /// <summary>当前激活的侧边面板 — 点击 Activity Bar 图标切换</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSessionPanelActive))]
    [NotifyPropertyChangedFor(nameof(IsFileTreePanelActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsPanelActive))]
    [NotifyPropertyChangedFor(nameof(IsGoalPanelActive))]
    [NotifyPropertyChangedFor(nameof(IsInterceptorPanelActive))]
    [NotifyPropertyChangedFor(nameof(IsChatRoomPanelActive))]
    [NotifyPropertyChangedFor(nameof(IsSidePanelExpanded))]
    [NotifyPropertyChangedFor(nameof(ActivePanelTitle))]
    private SidePanelKind _activeSidePanel = SidePanelKind.Sessions;

    /// <summary>当前面板停靠位置 — Left(默认)/Right,拖拽面板标题栏切换</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelDockedLeft))]
    [NotifyPropertyChangedFor(nameof(IsPanelDockedRight))]
    private DockPosition _activePanelDock = DockPosition.Left;

    /// <summary>面板是否停靠在左侧</summary>
    public bool IsPanelDockedLeft => ActivePanelDock == DockPosition.Left;

    /// <summary>面板是否停靠在右侧</summary>
    public bool IsPanelDockedRight => ActivePanelDock == DockPosition.Right;

    /// <summary>面板是否固定(固定后不可拖拽)</summary>
    [ObservableProperty]
    private bool _isPanelPinned;

    /// <summary>当前面板标题 — 根据ActiveSidePanel返回对应标题</summary>
    public string ActivePanelTitle => ActiveSidePanel switch {
        SidePanelKind.Sessions => "会话列表",
        SidePanelKind.FileTree => "目录树",
        SidePanelKind.Settings => "设置",
        SidePanelKind.Goal => "🎯 goal 控制",
        SidePanelKind.Interceptor => "🛡 AI 工具拦截器",
        SidePanelKind.ChatRoom => "💬 聊天室",
        _ => string.Empty
    };

    /// <summary>侧边面板区宽度 — 可拖拽调整,带磁吸(低于阈值自动折叠)</summary>
    [ObservableProperty]
    private double _sidePanelWidth = SidePanelDefaultWidth;

    /// <summary>会话面板是否激活(图标高亮)</summary>
    public bool IsSessionPanelActive => ActiveSidePanel == SidePanelKind.Sessions;

    /// <summary>目录树面板是否激活(图标高亮)</summary>
    public bool IsFileTreePanelActive => ActiveSidePanel == SidePanelKind.FileTree;

    /// <summary>设置面板是否激活(图标高亮)</summary>
    public bool IsSettingsPanelActive => ActiveSidePanel == SidePanelKind.Settings;

    /// <summary>goal 面板是否激活(图标高亮)</summary>
    public bool IsGoalPanelActive => ActiveSidePanel == SidePanelKind.Goal;

    /// <summary>拦截器面板是否激活(图标高亮)</summary>
    public bool IsInterceptorPanelActive => ActiveSidePanel == SidePanelKind.Interceptor;

    /// <summary>聊天室面板是否激活(图标高亮)</summary>
    public bool IsChatRoomPanelActive => ActiveSidePanel == SidePanelKind.ChatRoom;

    /// <summary>侧边面板是否展开</summary>
    public bool IsSidePanelExpanded => ActiveSidePanel != SidePanelKind.None;

    /// <summary>Secondary Side Bar 是否展开 — Primary 对侧边栏</summary>
    [ObservableProperty]
    private bool _isSecondarySideBarOpen;

    /// <summary>Secondary Side Bar 宽度</summary>
    [ObservableProperty]
    private double _secondarySideBarWidth = 236;

    /// <summary>Secondary Side Bar 显示的面板类型</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSecondaryFileTree))]
    [NotifyPropertyChangedFor(nameof(IsSecondarySessions))]
    private SidePanelKind _secondarySidePanelKind = SidePanelKind.FileTree;

    /// <summary>Secondary Side Bar 是否显示目录树</summary>
    public bool IsSecondaryFileTree => SecondarySidePanelKind == SidePanelKind.FileTree;

    /// <summary>Secondary Side Bar 是否显示会话列表</summary>
    public bool IsSecondarySessions => SecondarySidePanelKind == SidePanelKind.Sessions;

    /// <summary>切换侧边面板 — 点击当前已激活的面板收起,点击另一个面板切换。
    /// SideBar 面板与主区独立,不互斥(参考 VSCode)。</summary>
    [RelayCommand]
    private void ToggleSidePanel(SidePanelKind? kind) {
        if (kind is null)
            return;
        if (ActiveSidePanel == kind) {
            ActiveSidePanel = SidePanelKind.None;
            SidePanelWidth = 0;
        } else {
            ActiveSidePanel = kind.Value;
            if (SidePanelWidth < SidePanelSnapCollapseThreshold)
                SidePanelWidth = SidePanelDefaultWidth;
        }
    }

    /// <summary>切换面板固定状态 — 固定后不可拖拽</summary>
    [RelayCommand]
    private void TogglePanelPin() => IsPanelPinned = !IsPanelPinned;

    /// <summary>关闭当前激活的 Dock 面板</summary>
    [RelayCommand]
    private void CloseActivePanel() {
        _dockFactory?.CloseActiveDockPanel();
    }

    /// <summary>切换 Secondary Side Bar — 开关右侧边栏</summary>
    [RelayCommand]
    private void ToggleSecondarySideBar() {
        IsSecondarySideBarOpen = !IsSecondarySideBarOpen;
    }

    /// <summary>侧边面板宽度变化时磁吸 — 宽度低于阈值自动折叠</summary>
    partial void OnSidePanelWidthChanged(double value) {
        if (ActiveSidePanel != SidePanelKind.None && value > 0 && value < SidePanelSnapCollapseThreshold) {
            ActiveSidePanel = SidePanelKind.None;
        } else if (ActiveSidePanel == SidePanelKind.None && value >= SidePanelSnapCollapseThreshold) {
            ActiveSidePanel = SidePanelKind.Sessions;
        }
    }

    // ===== 目录树数据源 =====

    /// <summary>目录树根节点列表</summary>
    [ObservableProperty]
    private IReadOnlyList<FileTreeItemVm> _fileTreeItems = [];

    /// <summary>目录树根路径(当前工作目录)</summary>
    [ObservableProperty]
    private string _fileTreeRootPath = "";

    /// <summary>加载目录树 — 读取当前工作目录的一级文件/文件夹,懒加载子目录</summary>
    public void LoadFileTree(string rootPath) {
        FileTreeRootPath = rootPath;
        FileTreeItemVm.LoadChildrenCallback = LoadDirectoryChildren;
        FileTreeItems = LoadDirectoryChildren(rootPath);
    }

    /// <summary>读取目录子项 — 文件夹优先排序,跳过隐藏文件和 .git/.jcc 等</summary>
    private static IReadOnlyList<FileTreeItemVm> LoadDirectoryChildren(string path) {
        try {
            if (!System.IO.Directory.Exists(path))
                return [];
            var entries = System.IO.Directory.EnumerateFileSystemEntries(path)
                .Where(p => !IsHiddenOrIgnored(p))
                .OrderByDescending(p => System.IO.Directory.Exists(p))
                .ThenBy(p => System.IO.Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .Take(200)
                .Select(p => {
                    var isFolder = System.IO.Directory.Exists(p);
                    return new FileTreeItemVm {
                        Name = System.IO.Path.GetFileName(p),
                        FullPath = p,
                        IsFolder = isFolder
                    };
                })
                .ToArray();
            return entries;
        } catch {
            return [];
        }
    }

    /// <summary>判断路径是否隐藏或应忽略 — 隐藏文件(以.开头但非..) + .git/.jcc/bin/obj/artifacts</summary>
    private static bool IsHiddenOrIgnored(string path) {
        var name = System.IO.Path.GetFileName(path);
        if (string.IsNullOrEmpty(name))
            return true;
        if (name.StartsWith('.') && name != "..")
            return true;
        return name is ".git" or ".jcc" or "bin" or "obj" or "artifacts" or "node_modules";
    }
}
