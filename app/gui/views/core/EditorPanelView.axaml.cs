namespace JoinCode.Gui.Views;

/// <summary>
/// 编辑器面板 UserControl — 内嵌在主区(非弹窗),多标签页 + 面包屑 + AvaloniaEdit 代码编辑器。
/// 支持分屏编辑:左右两组独立标签页,用 GridSplitter 分割。
/// 标签页列表和活跃标签由 MainViewModel 管理,本控件负责同步 AvaloniaEdit.Text 与活跃标签 Content。
/// </summary>
public sealed partial class EditorPanelView : UserControl {
    private MainViewModel? _vm;
    private bool _suppressTextChanged;
    private bool _suppressTextChanged2;

    /// <summary>初始化 EditorPanelView 实例</summary>
    public EditorPanelView() {
        InitializeComponent();
        ConfigureEditorOptions(Editor);
        ConfigureEditorOptions(Editor2);
        Editor.TextChanged += OnEditorTextChanged;
        Editor2.TextChanged += OnEditor2TextChanged;
        _searchPanel = AvaloniaEdit.Search.SearchPanel.Install(Editor);
        _searchPanel2 = AvaloniaEdit.Search.SearchPanel.Install(Editor2);
        _foldingManager = AvaloniaEdit.Folding.FoldingManager.Install(Editor.TextArea);
        _foldingManager2 = AvaloniaEdit.Folding.FoldingManager.Install(Editor2.TextArea);
    }

    /// <summary>配置编辑器选项 — 自动缩进+制表符4空格+允许自动换行</summary>
    private static void ConfigureEditorOptions(AvaloniaEdit.TextEditor editor) {
        editor.Options.IndentationSize = 4;
        editor.Options.AllowScrollBelowDocument = true;
        editor.Options.WordWrapIndentation = 4;
    }

    private readonly AvaloniaEdit.Search.SearchPanel? _searchPanel;
    private readonly AvaloniaEdit.Search.SearchPanel? _searchPanel2;
    private readonly AvaloniaEdit.Folding.FoldingManager? _foldingManager;
    private readonly AvaloniaEdit.Folding.FoldingManager? _foldingManager2;

    /// <summary>DataContext 变更时订阅 ViewModel 属性变化</summary>
    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is not null) {
            _vm.PropertyChanged += OnVmPropertyChanged;
            UpdateEditorFromActiveTab();
            UpdateEditor2FromActiveTab();
        }
    }

    /// <summary>ViewModel 属性变化时联动编辑器 — 活跃标签变更时同步内容</summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(MainViewModel.ActiveEditorTab))
            UpdateEditorFromActiveTab();
        else if (e.PropertyName == nameof(MainViewModel.ActiveEditorTab2))
            UpdateEditor2FromActiveTab();
    }

    /// <summary>从活跃标签页同步内容到编辑器组1 — 切换标签时加载对应文件内容+语法高亮</summary>
    private void UpdateEditorFromActiveTab() {
        if (_vm?.ActiveEditorTab is not { } tab)
            return;
        _suppressTextChanged = true;
        Editor.Text = tab.Content;
        Minimap.Text = tab.Content;
        ApplySyntaxHighlighting(Editor, tab.FilePath);
        _suppressTextChanged = false;
        UpdateBreadcrumb(BreadcrumbItems, tab.FilePath);
    }

    /// <summary>从活跃标签页同步内容到编辑器组2</summary>
    private void UpdateEditor2FromActiveTab() {
        if (_vm?.ActiveEditorTab2 is not { } tab)
            return;
        _suppressTextChanged2 = true;
        Editor2.Text = tab.Content;
        ApplySyntaxHighlighting(Editor2, tab.FilePath);
        _suppressTextChanged2 = false;
        UpdateBreadcrumb(BreadcrumbItems2, tab.FilePath);
    }

    /// <summary>按文件扩展名应用语法高亮 — 使用 AvaloniaEdit 内置 HighlightingManager</summary>
    private static void ApplySyntaxHighlighting(AvaloniaEdit.TextEditor editor, string filePath) {
        var ext = System.IO.Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
            return;
        var def = AvaloniaEdit.Highlighting.HighlightingManager.Instance.GetDefinitionByExtension(ext);
        if (def is not null)
            editor.SyntaxHighlighting = def;
    }

    /// <summary>设置面包屑 — 从根目录到文件名的路径段</summary>
    private static void UpdateBreadcrumb(ItemsControl breadcrumb, string filePath) {
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
        breadcrumb.ItemsSource = segments;
    }

    /// <summary>编辑器组1内容变更时同步到活跃标签页 Content + 标记已修改 + 同步 Minimap</summary>
    private void OnEditorTextChanged(object? sender, EventArgs e) {
        if (_suppressTextChanged || _vm?.ActiveEditorTab is not { } tab)
            return;
        tab.Content = Editor.Text;
        tab.IsModified = true;
        Minimap.Text = Editor.Text;
    }

    /// <summary>编辑器组2内容变更时同步到活跃标签页2 Content + 标记已修改</summary>
    private void OnEditor2TextChanged(object? sender, EventArgs e) {
        if (_suppressTextChanged2 || _vm?.ActiveEditorTab2 is not { } tab)
            return;
        tab.Content = Editor2.Text;
        tab.IsModified = true;
    }

    /// <summary>标签页双击 — 固定标签(取消预览状态)</summary>
    private void OnTabDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) {
        if (sender is Button btn && btn.DataContext is EditorTabVm tab && _vm is not null)
            _vm.PinTabCommand.Execute(tab);
    }

    /// <summary>标签页中键点击 — 关闭标签页(VSCode 风格)</summary>
    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e) {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed) {
            if (sender is Button btn && btn.DataContext is EditorTabVm tab && _vm is not null) {
                _vm.CloseEditorTabCommand.Execute(tab);
                e.Handled = true;
            }
            return;
        }
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && sender is Button dragBtn && dragBtn.DataContext is EditorTabVm dragTab) {
            _draggingTab = dragTab;
            e.Pointer.Capture(dragBtn);
        }
    }

    /// <summary>拖拽中标签页</summary>
    private EditorTabVm? _draggingTab;

    /// <summary>标签页拖拽移动 — 检测目标位置并重排序</summary>
    private void OnTabPointerMoved(object? sender, PointerEventArgs e) {
        if (_draggingTab is null || _vm is null)
            return;
        var pos = e.GetCurrentPoint(this).Position;
        var target = FindTabAtPosition(pos);
        if (target is not null && target != _draggingTab)
            _vm.ReorderEditorTabCommand.Execute((_draggingTab, target));
    }

    /// <summary>标签页拖拽释放 — 结束拖拽</summary>
    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e) {
        if (_draggingTab is null)
            return;
        _draggingTab = null;
        e.Pointer.Capture(null);
    }

    /// <summary>在指定位置查找标签页</summary>
    private EditorTabVm? FindTabAtPosition(Point pos) {
        if (_vm is null)
            return null;
        foreach (var tab in _vm.EditorTabs) {
            if (this.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.DataContext == tab && b.Classes.Contains("editorTab")) is { } btn) {
                var bounds = btn.Bounds;
                var origin = (btn.TransformToVisual(this) ?? default).Transform(default);
                if (pos.X >= origin.X && pos.X <= origin.X + bounds.Width)
                    return tab;
            }
        }
        return null;
    }

    /// <summary>标签页栏滚轮 — 切换标签页(VSCode 风格)</summary>
    private void OnTabWheelChanged(object? sender, PointerWheelEventArgs e) {
        if (_vm is null || _vm.EditorTabs.Count == 0)
            return;
        var current = _vm.EditorTabs.IndexOf(_vm.ActiveEditorTab);
        if (current < 0)
            return;
        var delta = e.Delta.Y > 0 ? -1 : 1;
        var next = (current + delta + _vm.EditorTabs.Count) % _vm.EditorTabs.Count;
        _vm.ActivateEditorTabCommand.Execute(_vm.EditorTabs[next]);
        e.Handled = true;
    }
}
