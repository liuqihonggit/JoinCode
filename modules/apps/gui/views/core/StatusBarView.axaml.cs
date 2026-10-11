namespace JoinCode.Gui.Views;

/// <summary>
/// 状态栏视图 — 全局运行状态 + 走马灯 + 后台代理 pill + 会话/消息/token 统计 + 模型徽章。
/// 从 MainWindow.axaml 提取（任务6 模块化），DataContext 自动继承 MainViewModel。
/// </summary>
public partial class StatusBarView : UserControl {
    /// <summary>当前 MainViewModel（供 XAML CompiledBindings 解析命令类型）</summary>
    public MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>构造状态栏视图</summary>
    public StatusBarView() => InitializeComponent();
}
