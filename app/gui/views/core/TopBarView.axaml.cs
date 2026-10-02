namespace JoinCode.Gui.Views;

/// <summary>
/// 顶部工具栏 UserControl — 主题切换/重新生成/清空/全部重置按钮 +
/// 连接选择/模型选择下拉 + 设置面板开关。纯绑定，无 code-behind 逻辑。
/// </summary>
public sealed partial class TopBarView : UserControl {
    private WorkbenchWindow? _workbenchWindow;
    /// <summary>初始化 TopBarView 实例</summary>
    public TopBarView() {
        InitializeComponent();
    }

    private void OnOpenFiles(object? sender, RoutedEventArgs e) => OpenWorkbench(0);
    private void OnOpenManagement(object? sender, RoutedEventArgs e) => OpenWorkbench(2);
    private void OpenWorkbench(int tab) {
        if (DataContext is not MainViewModel vm || TopLevel.GetTopLevel(this) is not Window owner) return;
        vm.Workbench.TabIndex = tab;
        if (_workbenchWindow is not null) { _workbenchWindow.Activate(); return; }
        _workbenchWindow = new WorkbenchWindow { DataContext = vm.Workbench };
        _workbenchWindow.Closed += (_, _) => _workbenchWindow = null;
        _workbenchWindow.Show(owner);
        _ = vm.Workbench.InitializeAsync();
    }
}
