namespace JoinCode.Gui.Views;

/// <summary>
/// 侧边栏 UserControl — 新建对话按钮 + 会话列表 + 底部状态。
/// 会话条目的 Tapped/DoubleTapped 事件路由到 VM 命令（选中/重命名）。
/// </summary>
public sealed partial class SidebarView : UserControl
{
    /// <summary>当前 MainViewModel（供 XAML CompiledBindings 解析命令类型）</summary>
    public MainViewModel? ViewModel => DataContext as MainViewModel;

    public SidebarView()
    {
        InitializeComponent();
    }

    /// <summary>单击会话条目：切换选中态（高亮当前会话）</summary>
    private void OnSessionSingleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Avalonia.StyledElement { DataContext: SessionItem session } && DataContext is MainViewModel vm)
            vm.SelectSessionCommand.Execute(session);
    }

    /// <summary>双击会话条目：请求重命名（实际由 VM 状态驱动内联编辑）</summary>
    private void OnSessionDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Avalonia.StyledElement { DataContext: SessionItem session } && DataContext is MainViewModel vm)
            vm.BeginRenameSessionCommand.Execute(session);
    }
}
