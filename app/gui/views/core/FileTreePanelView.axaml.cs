namespace JoinCode.Gui.Views;

/// <summary>
/// 目录树面板 UserControl — 展示当前工作目录的文件结构,支持懒加载子目录。
/// 纯绑定,无 code-behind 逻辑(数据由 MainViewModel.FileTreeItems 提供)。
/// </summary>
public sealed partial class FileTreePanelView : UserControl {
    /// <summary>初始化 FileTreePanelView 实例</summary>
    public FileTreePanelView() {
        InitializeComponent();
    }
}
