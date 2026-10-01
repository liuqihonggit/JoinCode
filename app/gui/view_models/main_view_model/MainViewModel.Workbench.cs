namespace JoinCode.Gui.ViewModels;

/// <summary>工作台统一使用当前活动会话，支持引擎热切换。</summary>
public sealed partial class MainViewModel {
    private WorkbenchViewModel? _workbench;
    /// <summary>工作台实例，按需创建。</summary>
    public WorkbenchViewModel Workbench => _workbench ??= new WorkbenchViewModel(this, () => _session);
}
