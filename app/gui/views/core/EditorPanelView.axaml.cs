namespace JoinCode.Gui.Views;

/// <summary>
/// 编辑器面板 UserControl — 内嵌在主区(非弹窗),多标签页 + 面包屑 + AvaloniaEdit 代码编辑器。
/// 标签页列表和活跃标签由 MainViewModel 管理,本控件负责同步 AvaloniaEdit.Text 与活跃标签 Content。
/// </summary>
public sealed partial class EditorPanelView : UserControl {
    private MainViewModel? _vm;
    private bool _suppressTextChanged;

    /// <summary>初始化 EditorPanelView 实例</summary>
    public EditorPanelView() {
        InitializeComponent();
        Editor.TextChanged += OnEditorTextChanged;
    }

    /// <summary>DataContext 变更时订阅 ViewModel 属性变化</summary>
    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is not null) {
            _vm.PropertyChanged += OnVmPropertyChanged;
            UpdateEditorFromActiveTab();
        }
    }

    /// <summary>ViewModel 属性变化时联动编辑器 — 活跃标签变更时同步内容</summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(MainViewModel.ActiveEditorTab))
            UpdateEditorFromActiveTab();
    }

    /// <summary>从活跃标签页同步内容到编辑器 — 切换标签时加载对应文件内容</summary>
    private void UpdateEditorFromActiveTab() {
        if (_vm?.ActiveEditorTab is not { } tab)
            return;
        _suppressTextChanged = true;
        Editor.Text = tab.Content;
        _suppressTextChanged = false;
        UpdateBreadcrumb(tab.FilePath);
    }

    /// <summary>设置面包屑 — 从根目录到文件名的路径段</summary>
    private void UpdateBreadcrumb(string filePath) {
        var segments = new List<string>();
        var dir = System.IO.Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(dir)) {
            segments.Add(System.IO.Path.GetFileName(dir));
            var parent = System.IO.Path.GetDirectoryName(dir);
            if (parent == dir)
                break;
            dir = parent;
        }
        segments.Reverse();
        segments.Add(System.IO.Path.GetFileName(filePath));
        BreadcrumbItems.ItemsSource = segments;
    }

    /// <summary>编辑器内容变更时同步到活跃标签页 Content + 标记已修改</summary>
    private void OnEditorTextChanged(object? sender, EventArgs e) {
        if (_suppressTextChanged || _vm?.ActiveEditorTab is not { } tab)
            return;
        tab.Content = Editor.Text;
        tab.IsModified = true;
    }

    /// <summary>标签页双击 — 固定标签(取消预览状态)</summary>
    private void OnTabDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) {
        if (sender is Button btn && btn.DataContext is EditorTabVm tab && _vm is not null)
            _vm.PinTabCommand.Execute(tab);
    }
}
