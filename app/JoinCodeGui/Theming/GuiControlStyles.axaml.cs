using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace JoinCode.Gui.Theming;

/// <summary>
/// 共享控件样式（设计语言单一数据源）— 真实 App 与 headless 测试经 <see cref="GuiAppResources.Register"/> 共用。
/// 编译型 Styles 类（x:Class + XamlIl），避免动态 XAML 加载破坏 NativeAOT 裁剪。
/// </summary>
public partial class GuiControlStyles : Styles
{
    public GuiControlStyles()
        => AvaloniaXamlLoader.Load(this);
}
