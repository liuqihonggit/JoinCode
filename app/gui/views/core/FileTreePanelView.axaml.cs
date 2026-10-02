namespace JoinCode.Gui.Views;

/// <summary>
/// 目录树面板 UserControl — 展示当前工作目录的文件结构,支持懒加载子目录。
/// 双击文件打开 EditorWindow 代码编辑器。
/// </summary>
public sealed partial class FileTreePanelView : UserControl {
    /// <summary>初始化 FileTreePanelView 实例</summary>
    public FileTreePanelView() {
        InitializeComponent();
    }

    /// <summary>双击文件项 — 打开 EditorWindow 编辑文件(仅文件,非文件夹)</summary>
    private void OnFileDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) {
        if (sender is not StackPanel panel)
            return;
        if (panel.DataContext is not ViewModels.FileTreeItemVm item)
            return;
        if (item.IsFolder)
            return;
        var window = new EditorWindow();
        window.OpenFile(item.FullPath);
        window.Show(this.GetVisualRoot() as Window);
    }
}
