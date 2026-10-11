namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 目录树节点 ViewModel — 文件/文件夹节点,支持懒加载子节点(展开时才读取目录内容)。
/// 用于 FileTreePanelView 的 TreeView 数据源,展示当前工作目录的文件结构。
/// </summary>
public sealed class FileTreeItemVm : ViewModelBase {
    private bool _isExpanded;
    private IReadOnlyList<FileTreeItemVm> _children = [];
    private bool _childrenLoaded;

    /// <summary>节点名称(文件/文件夹名,不含路径)</summary>
    public required string Name { get; init; }

    /// <summary>完整路径(用于打开文件/展开文件夹)</summary>
    public required string FullPath { get; init; }

    /// <summary>是否为文件夹(文件夹可展开,文件不可)</summary>
    public required bool IsFolder { get; init; }

    /// <summary>是否已展开(文件夹专属,驱动子节点加载)</summary>
    public bool IsExpanded {
        get => _isExpanded;
        set {
            if (SetProperty(ref _isExpanded, value) && value && !_childrenLoaded)
                LoadChildren();
        }
    }

    /// <summary>子节点列表(文件夹专属,展开时懒加载)</summary>
    public IReadOnlyList<FileTreeItemVm> Children => _children;

    /// <summary>图标字符(文件夹 📁 / 文件根据扩展名)</summary>
    public string Icon => IsFolder ? "📁" : GetFileIcon(Name);

    /// <summary>加载子节点 — 读取目录内容(文件夹专属)。由父面板注入加载回调。</summary>
    public void LoadChildren() {
        if (_childrenLoaded || !IsFolder)
            return;
        _childrenLoaded = true;
        if (LoadChildrenCallback is not null)
            _children = LoadChildrenCallback(FullPath);
        OnPropertyChanged(nameof(Children));
    }

    /// <summary>子节点加载回调 — 由 FileTreePanelView 注入,避免 ViewModel 直接依赖文件系统</summary>
    public static Func<string, IReadOnlyList<FileTreeItemVm>>? LoadChildrenCallback { get; set; }

    /// <summary>根据文件扩展名返回图标 emoji</summary>
    private static string GetFileIcon(string name) {
        var ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
        return ext switch {
            ".cs" => "🔷",
            ".xaml" => "📐",
            ".json" => "📋",
            ".md" => "📝",
            ".csproj" or ".sln" or ".slnx" => "📦",
            ".ts" or ".js" => "🟨",
            ".py" => "🐍",
            ".rs" => "🦀",
            ".txt" => "📄",
            _ => "📄"
        };
    }
}
