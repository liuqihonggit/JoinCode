using Avalonia.Controls;

namespace JoinCode.Gui.Views;

/// <summary>
/// 顶部工具栏 UserControl — 主题切换/重新生成/清空/全部重置按钮 +
/// 连接选择/模型选择下拉 + 设置面板开关。纯绑定，无 code-behind 逻辑。
/// </summary>
public sealed partial class TopBarView : UserControl
{
    public TopBarView()
    {
        InitializeComponent();
    }
}
