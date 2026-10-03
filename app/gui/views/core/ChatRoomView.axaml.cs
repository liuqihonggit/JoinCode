namespace JoinCode.Gui.Views;

/// <summary>
/// 聊天室面板 — 展示多 Agent 协作时的成员列表与广播消息流（对标 QQ 群聊天面板）。
/// 鼠标离开时自动关闭，复用 <see cref="MainViewModel.IsChatRoomPanelOpen"/> 状态。
/// </summary>
public sealed partial class ChatRoomView : UserControl {
    /// <summary>初始化 ChatRoomView 实例</summary>
    public ChatRoomView() => InitializeComponent();

    /// <summary>鼠标离开时自动关闭聊天室面板</summary>
    private void OnPointerExited(object? sender, Avalonia.Input.PointerEventArgs e) {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.IsChatRoomPanelOpen = false;
    }
}
