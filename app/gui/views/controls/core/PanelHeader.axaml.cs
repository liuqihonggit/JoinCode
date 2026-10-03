namespace JoinCode.Gui.Views.Controls;

/// <summary>
/// 面板标题栏 — 统一的拖拽手柄+标题+关闭按钮。
/// 拖拽手柄发起 DragDrop,由 MainWindow 接收并切换面板停靠位置。
/// </summary>
public partial class PanelHeader : UserControl {
    /// <summary>初始化 PanelHeader 实例</summary>
    public PanelHeader() => InitializeComponent();

    /// <summary>拖拽手柄按下时发起 DragDrop — 携带面板类型信息</summary>
    private void OnDragHandlePressed(object? sender, PointerPressedEventArgs e) {
        if (DataContext is not PanelHeaderVm vm)
            return;
        var data = new DataObject();
        data.Set("PanelDrag", vm.Title);
        _ = DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }
}
