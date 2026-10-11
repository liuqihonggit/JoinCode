namespace JoinCode.Gui.Views;

/// <summary>
/// Diff 视图 — 全屏展示 AI 修改代码的前后对比，含文件名、增删统计、DiffViewer 渲染。
/// 由消息卡片中"查看完整 diff"按钮触发，Popup 弹出。
/// </summary>
public sealed partial class DiffView : UserControl {
    /// <summary>初始化 DiffView 实例</summary>
    public DiffView() => InitializeComponent();

    /// <summary>点击 diff 文件路径 — 在资源管理器中定位文件</summary>
    private void OnDiffFilePathTapped(object? sender, Avalonia.Input.TappedEventArgs e) {
        if (DataContext is not ViewModels.MainViewModel vm)
            return;
        if (string.IsNullOrEmpty(vm.DiffFilePath))
            return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{vm.DiffFilePath}\"",
            UseShellExecute = true
        });
    }
}
