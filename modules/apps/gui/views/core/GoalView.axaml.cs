namespace JoinCode.Gui.Views;

/// <summary>goal 控制面板 — 从 ActivityBar 点击🎯展开的 SideBar 面板。</summary>
public partial class GoalView : UserControl {
    /// <summary>初始化 GoalView 实例</summary>
    public GoalView() => InitializeComponent();

    /// <summary>拖拽手柄按下时发起 DragDrop — 携带面板标识</summary>
    private void OnDragHandlePressed(object? sender, PointerPressedEventArgs e) {
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DataFormat.CreateInProcessFormat<string>("PanelDrag"), nameof(GoalView)));
        _ = DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }
}
