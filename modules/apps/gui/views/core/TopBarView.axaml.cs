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

    /// <summary>显示快捷键列表</summary>
    private void OnShowShortcuts(object? sender, RoutedEventArgs e) {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var msg = "快捷键:\n  Ctrl+Enter — 发送消息\n  Ctrl+N — 新建会话\n  Ctrl+Shift+P — 命令面板\n  ESC — 停止生成\n  双击 ESC — 终止手势\n  Ctrl+S — 保存文件\n  Ctrl+Shift+E — 切换编辑器";
        var dlg = new ConfirmDialogWindow(msg) { Title = "快捷键" };
        dlg.OkButton.Content = "关闭";
        dlg.CancelButton.IsVisible = false;
        _ = dlg.ShowDialog(owner);
    }

    /// <summary>显示关于信息</summary>
    private void OnShowAbout(object? sender, RoutedEventArgs e) {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var msg = "JoinCode v1.0";
        var dlg = new ConfirmDialogWindow(msg) { Title = "关于 JoinCode" };
        dlg.OkButton.Content = "关闭";
        dlg.CancelButton.IsVisible = false;
        _ = dlg.ShowDialog(owner);
    }
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
