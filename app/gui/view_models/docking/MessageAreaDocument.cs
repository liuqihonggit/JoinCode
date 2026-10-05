namespace JoinCode.Gui.ViewModels.Docking;

/// <summary>
/// 消息区 Document — Dock 中心区域，包装主消息列表+输入栏。
/// </summary>
public sealed class MessageAreaDocument : Document {
    /// <summary>关联的会话条目 — 供 HeaderTemplate 绑定子会话角标（null 为默认空文档）</summary>
    public SessionItem? Session { get; set; }

    /// <summary>主 ViewModel — 供 HeaderTemplate 绑定 SelectSessionCommand（Context 转型）</summary>
    public MainViewModel? Vm => Context as MainViewModel;

    /// <summary>选择子会话命令 — 委托到 Vm.SelectSessionCommand，供角标 Popup 内 Button 绑定</summary>
    public System.Windows.Input.ICommand? SelectSubSessionCommand => Vm?.SelectSessionCommand;
}
