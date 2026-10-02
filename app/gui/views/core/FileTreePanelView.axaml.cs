namespace JoinCode.Gui.Views;

/// <summary>
/// 目录树面板 UserControl — 展示当前工作目录的文件结构,支持懒加载子目录。
/// 双击文件在内嵌编辑器面板中打开(非弹窗)。
/// </summary>
public sealed partial class FileTreePanelView : UserControl {
    /// <summary>初始化 FileTreePanelView 实例</summary>
    public FileTreePanelView() {
        InitializeComponent();
    }

    /// <summary>双击文件项 — 在内嵌编辑器中固定打开文件(非预览)</summary>
    private void OnFileDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) {
        if (sender is not StackPanel panel)
            return;
        if (panel.DataContext is not ViewModels.FileTreeItemVm item)
            return;
        if (item.IsFolder)
            return;
        if (DataContext is ViewModels.MainViewModel vm)
            vm.OpenEditorFilePinned(item.FullPath);
    }
}
