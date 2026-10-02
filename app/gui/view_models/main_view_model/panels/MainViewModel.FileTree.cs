namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 目录树+可折叠面板 partial — 管理目录树数据源和侧边面板折叠/展开/磁吸状态。
/// 两个侧边面板(会话栏+目录树)可独立折叠,折叠时只显示图标列(48px),展开时完整面板(236px)。
/// 磁吸:拖拽宽度靠近阈值时自动吸附到折叠/展开态。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>折叠态面板宽度(只显示图标列)</summary>
    private const double CollapsedPanelWidth = 48;

    /// <summary>展开态面板宽度(完整面板)</summary>
    private const double ExpandedPanelWidth = 236;

    /// <summary>磁吸阈值 — 拖拽宽度低于此值自动折叠</summary>
    private const double SnapCollapseThreshold = 80;

    // ===== 会话栏面板 =====

    /// <summary>会话栏面板是否折叠</summary>
    [ObservableProperty]
    private bool _isSessionPanelCollapsed;

    /// <summary>会话栏面板宽度(折叠=48,展开=236,磁吸驱动)</summary>
    [ObservableProperty]
    private double _sessionPanelWidth = ExpandedPanelWidth;

    /// <summary>切换会话栏面板折叠/展开</summary>
    [RelayCommand]
    private void ToggleSessionPanel() {
        IsSessionPanelCollapsed = !IsSessionPanelCollapsed;
        SessionPanelWidth = IsSessionPanelCollapsed ? CollapsedPanelWidth : ExpandedPanelWidth;
    }

    /// <summary>会话栏面板宽度变化时磁吸 — 宽度低于 SnapCollapseThreshold 自动折叠</summary>
    partial void OnSessionPanelWidthChanged(double value) {
        if (!IsSessionPanelCollapsed && value > 0 && value < SnapCollapseThreshold) {
            IsSessionPanelCollapsed = true;
            SessionPanelWidth = CollapsedPanelWidth;
        } else if (IsSessionPanelCollapsed && value >= SnapCollapseThreshold) {
            IsSessionPanelCollapsed = false;
        }
    }

    // ===== 目录树面板 =====

    /// <summary>目录树面板是否折叠</summary>
    [ObservableProperty]
    private bool _isFileTreePanelCollapsed;

    /// <summary>目录树面板宽度(折叠=48,展开=236,磁吸驱动)</summary>
    [ObservableProperty]
    private double _fileTreePanelWidth = ExpandedPanelWidth;

    /// <summary>切换目录树面板折叠/展开</summary>
    [RelayCommand]
    private void ToggleFileTreePanel() {
        IsFileTreePanelCollapsed = !IsFileTreePanelCollapsed;
        FileTreePanelWidth = IsFileTreePanelCollapsed ? CollapsedPanelWidth : ExpandedPanelWidth;
    }

    /// <summary>目录树面板宽度变化时磁吸 — 宽度低于 SnapCollapseThreshold 自动折叠</summary>
    partial void OnFileTreePanelWidthChanged(double value) {
        if (!IsFileTreePanelCollapsed && value > 0 && value < SnapCollapseThreshold) {
            IsFileTreePanelCollapsed = true;
            FileTreePanelWidth = CollapsedPanelWidth;
        } else if (IsFileTreePanelCollapsed && value >= SnapCollapseThreshold) {
            IsFileTreePanelCollapsed = false;
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
