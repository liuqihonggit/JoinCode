using Avalonia.Controls;

namespace JoinCode.Gui.Views;

/// <summary>
/// 设置面板 UserControl — 对话参数（温度/最大长度/字号/流式/推理力度/系统提示词）
/// 与快捷操作。所有控件通过绑定连接 MainViewModel，无 code-behind 逻辑。
/// </summary>
public sealed partial class SettingsPanelView : UserControl
{
    public SettingsPanelView()
    {
        InitializeComponent();
    }
}
