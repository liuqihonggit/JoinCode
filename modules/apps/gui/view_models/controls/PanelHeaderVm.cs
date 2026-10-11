namespace JoinCode.Gui.ViewModels;

/// <summary>面板标题栏 ViewModel — 标题+关闭命令+拖拽手柄。</summary>
public sealed partial class PanelHeaderVm : ObservableObject {
    /// <summary>面板标题</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>关闭命令 — 点击✕执行(由父面板提供)</summary>
    public System.Windows.Input.ICommand? CloseCommand { get; set; }
}
