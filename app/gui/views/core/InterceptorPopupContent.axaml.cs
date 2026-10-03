namespace JoinCode.Gui.Views;

/// <summary>
/// AI 工具拦截器 Popup 内容 — 纯绑定 UserControl,从 TopBar 迁移到 Activity Bar。
/// </summary>
public sealed partial class InterceptorPopupContent : UserControl {
    /// <summary>初始化 InterceptorPopupContent 实例</summary>
    public InterceptorPopupContent() {
        InitializeComponent();
    }

    /// <summary>鼠标离开时自动关闭拦截器面板</summary>
    private void OnPointerExited(object? sender, Avalonia.Input.PointerEventArgs e) {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.IsInterceptorPanelOpen = false;
    }
}
