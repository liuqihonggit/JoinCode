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

    /// <summary>右键菜单 — 在资源管理器中显示文件/文件夹</summary>
    private void OnOpenInExplorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
        if (GetTreeItemFromMenu(sender) is not { } item)
            return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{item.FullPath}\"",
            UseShellExecute = true
        });
    }

    /// <summary>右键菜单 — 复制完整路径到剪贴板</summary>
    private void OnCopyPath(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
        if (GetTreeItemFromMenu(sender) is not { } item)
            return;
        var clip = Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard;
        _ = clip?.SetTextAsync(item.FullPath);
    }

    /// <summary>右键菜单 — 复制相对路径到剪贴板</summary>
    private void OnCopyRelativePath(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
        if (GetTreeItemFromMenu(sender) is not { } item)
            return;
        if (DataContext is not ViewModels.MainViewModel vm)
            return;
        var rel = System.IO.Path.GetRelativePath(vm.FileTreeRootPath, item.FullPath);
        var clip = Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard;
        _ = clip?.SetTextAsync(rel);
    }

    /// <summary>从菜单项向上查找 TreeItem ViewModel</summary>
    private ViewModels.FileTreeItemVm? GetTreeItemFromMenu(object? sender) {
        if (sender is not Avalonia.Controls.MenuItem mi)
            return null;
        return mi.DataContext as ViewModels.FileTreeItemVm;
    }
}
