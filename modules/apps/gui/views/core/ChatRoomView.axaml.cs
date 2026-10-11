namespace JoinCode.Gui.Views;

/// <summary>
/// 聊天室面板 — 展示多 Agent 协作时的成员列表与广播消息流（对标 QQ 群聊天面板）。
/// 作为 SideBar 面板从左侧展开，点击 ActivityBar 👥 图标切换。
/// </summary>
public sealed partial class ChatRoomView : UserControl {
    /// <summary>初始化 ChatRoomView 实例</summary>
    public ChatRoomView() => InitializeComponent();
}
