namespace JoinCode.Gui.Views;

/// <summary>
/// 设置面板 UserControl — 对话参数（温度/最大长度/字号/流式/推理力度/系统提示词）
/// 与快捷操作。所有控件通过绑定连接 MainViewModel。
/// 快捷键录制（需求3）在隧道 KeyDown 中捕获：录制中的项接收任意键组合。
/// </summary>
public sealed partial class SettingsPanelView : UserControl
{
    public SettingsPanelView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnRecordingKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>需求3：快捷键录制 — 录制中的项捕获按键组合，跳过纯修饰键</summary>
    private void OnRecordingKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        foreach (var item in vm.HotkeyItems)
        {
            if (!item.IsRecording)
                continue;
            // 纯修饰键不触发录制完成（等用户按实际键）
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
                return;
            e.Handled = true;
            var mods = e.KeyModifiers;
            var sb = new System.Text.StringBuilder();
            if ((mods & KeyModifiers.Control) != 0)
                sb.Append("Ctrl+");
            if ((mods & KeyModifiers.Shift) != 0)
                sb.Append("Shift+");
            if ((mods & KeyModifiers.Alt) != 0)
                sb.Append("Alt+");
            sb.Append(e.Key.ToString());
            vm.ApplyRecordedHotkey(item, sb.ToString());
            return;
        }
    }
}
