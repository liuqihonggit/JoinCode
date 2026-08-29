namespace JoinCode.Gui.Tests.Theming;

/// <summary>
/// GuiPalette 配色合规性测试 —— 用 WCAG 相对亮度计算所有"文字/背景"语义对对比度，
/// 校验 ≥ 4.5:1（AA 普通文字）。防止出现黑字配深底之类的低对比配色侵入。
/// </summary>
[Collection("GuiUiSequential")]
public class GuiPaletteContrastTests
{
    /// <summary>普通文字最低对比度（WCAG AA）</summary>
    private const double MinContrast = 4.5;

    private static Color C(string hex) => Color.Parse(hex);

    /// <summary>校验"文字色 相对 背景色"对比度足够。</summary>
    private static void AssertContrast(string textual, string back, string label, double min = MinContrast)
    {
        var ratio = GuiPalette.ContrastRatio(C(textual), C(back));
        ratio.Should().BeGreaterThanOrEqualTo(min, $"{label}: {textual} on {back}");
    }

    [Fact]
    public void Dark_TextOnSurfaces_MeetsAAContrast()
    {
        var s = GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Dark);
        AssertContrast(s.PrimaryText, s.WindowBackground, "主文字/窗口底");
        AssertContrast(s.SecondaryText, s.WindowBackground, "次文字/窗口底");
        AssertContrast(s.MutedText, s.WindowBackground, "弱文字/窗口底");
        AssertContrast(s.SidebarTitle, s.SidebarBackground, "侧边标题/侧边底");
        AssertContrast(s.MutedText, s.SidebarBackground, "弱文字/侧边底");
        AssertContrast(s.MutedText, s.TopBarBackground, "弱文字/顶栏底");
        AssertContrast(s.MutedText, s.StatusBarBackground, "弱文字/状态栏底");
        AssertContrast(s.PrimaryText, s.SettingsBackground, "设置标题/设置底");
    }

    [Fact]
    public void Button_TextOnBackground_MeetsAAContrast()
    {
        var dark = GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Dark);
        var light = GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Light);

        AssertContrast(dark.ButtonForeground, dark.ButtonBackground, "深:按钮文字/按钮底");
        AssertContrast(dark.ButtonForeground, dark.ButtonHover, "深:按钮文字/按钮悬浮底");
        AssertContrast(dark.ButtonForeground, dark.ButtonPressed, "深:按钮文字/按钮按下底");
        AssertContrast(light.ButtonForeground, light.ButtonBackground, "浅:按钮文字/按钮底");
        AssertContrast(light.ButtonForeground, light.ButtonHover, "浅:按钮文字/按钮悬停底");
        AssertContrast(light.ButtonForeground, light.ButtonPressed, "浅:按钮文字/按钮按下底");
    }

    [Fact]
    public void Light_Text_Textures_AlwaysAAContrast()
    {
        var s = GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Light);
        AssertContrast(s.PrimaryText, s.WindowBackground, "主文字/窗口底");
        AssertContrast(s.SecondaryText, s.WindowBackground, "次文字/窗口底");
        AssertContrast(s.MutedText, s.WindowBackground, "弱文字/窗口底");
        AssertContrast(s.SidebarTitle, s.SidebarBackground, "侧边栏标题/侧边底");
        AssertContrast(s.MutedText, s.SidebarBackground, "弱文字/侧边底");
        AssertContrast(s.MutedText, s.TopBarBackground, "弱文字/顶栏底");
        AssertContrast(s.MutedText, s.StatusBarBackground, "弱文字/状态栏底");
        AssertContrast(s.PrimaryText, s.SettingsBackground, "设置标题/设置底");
    }

    [Fact]
    public void BubbleText_EachKind_ContrastsOnItsSurface()
    {
        var dark = GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Dark);
        var light = GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Light);

        // 深色主题：正文/用户气泡须配浅色文字（默认主题文字色）
        AssertContrast(dark.PrimaryText, dark.BubbleText, "深:正文文字/正文气泡");
        AssertContrast(dark.PrimaryText, dark.BubbleUser, "深:正文/用户气泡");
        AssertContrast(dark.PrimaryText, dark.BubbleToolCall, "深:正文/工具气泡");
        AssertContrast(dark.PrimaryText, dark.BubbleToolResult, "深:正文/结果气泡");
        AssertContrast(dark.ThinkingLabel, dark.BubbleThinking, "深:思考标签/思考气泡");
        AssertContrast(dark.ToolLabel, dark.BubbleToolCall, "深:工具标签/工具气泡");
        AssertContrast(dark.ToolResult, dark.BubbleToolResult, "深:工具结果/结果气泡");

        // 浅色主题：正文/浅色气泡须配深色文字
        AssertContrast(light.PrimaryText, light.BubbleText, "浅:正文/正文气泡");
        AssertContrast(light.PrimaryText, light.BubbleUser, "浅:正文/用户气泡");
        AssertContrast(light.ThinkingLabel, light.BubbleThinking, "浅:思考标签/思考气泡");
        AssertContrast(light.ToolLabel, light.BubbleToolCall, "浅:工具标签/工具气泡");
        AssertContrast(light.ToolResult, light.BubbleToolResult, "浅:工具结果/结果气泡");
    }

    [Fact]
    public void StatusIndicators_LargeEnoughForUiContrast()
    {
        var dark = GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Dark);
        var light = GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Light);
        // 指示器为非文字 UI 元件，按 3:1 校验
        AssertContrast(dark.SuccessText, dark.StatusBarBackground, "深:就绪/状态栏底", 3);
        AssertContrast(dark.BusyText, dark.StatusBarBackground, "深:忙碌/状态栏底", 3);
        AssertContrast(light.SuccessText, light.StatusBarBackground, "浅:就绪/状态栏底", 3);
        AssertContrast(light.BusyText, light.StatusBarBackground, "浅:忙碌/状态栏底", 3);
    }

    [Fact]
    public void DarkAndLight_ProvideContrastingAccentOnSurfaces()
    {
        var dark = GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Dark);
        var light = GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Light);
        AssertContrast(dark.AccentText, dark.SidebarBackground, "深:强调/侧边底", 3);
        AssertContrast(light.AccentText, light.StatusBarBackground, "浅:强调/状态栏底", 3);
    }

    [Fact]
    public void Dialog_TextOnWindowBackground_MeetsAAContrast()
    {
        var dark = GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Dark);
        var light = GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Light);

        AssertContrast(dark.PrimaryText, dark.WindowBackground, "深:弹窗标题/窗口底");
        AssertContrast(dark.SecondaryText, dark.WindowBackground, "深:弹窗问题/窗口底");
        AssertContrast(dark.ButtonForeground, dark.ButtonBackground, "深:弹窗按钮文字/按钮底");
        AssertContrast(dark.AccentText, dark.ButtonBackground, "深:弹窗确认按钮/按钮底", 3);
        AssertContrast(dark.PrimaryText, dark.InputBarBackground, "深:弹窗输入框文字/输入框底");

        AssertContrast(light.PrimaryText, light.WindowBackground, "浅:弹窗标题/窗口底");
        AssertContrast(light.SecondaryText, light.WindowBackground, "浅:弹窗问题/窗口底");
        AssertContrast(light.ButtonForeground, light.ButtonBackground, "浅:弹窗按钮文字/按钮底");
        AssertContrast(light.AccentText, light.ButtonBackground, "浅:弹窗确认按钮/按钮底", 3);
        AssertContrast(light.PrimaryText, light.InputBarBackground, "浅:弹窗输入框文字/输入框底");
    }

    /// <summary>取转换器输出的画刷色值。</summary>
    private static Color BrushColor(object result)
    {
        result.Should().BeAssignableTo<ISolidColorBrush>();
        return ((ISolidColorBrush)result).Color;
    }

    [AvaloniaFact]
    public void RoleAndStatusConverters_UnderLight_ReturnLightColors()
    {
        var light = GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Light);

        try
        {
            GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Light;
            var role = new BoolToRoleBrushConverter();
            var status = new StatusToBrushConverter();

            BrushColor(role.Convert(true, typeof(ISolidColorBrush), null, null!))
                .ToString().Should().Be(Color.Parse(light.RoleUser).ToString());
            BrushColor(status.Convert(StatusKind.Error, typeof(ISolidColorBrush), null, null!))
                .ToString().Should().Be(Color.Parse(light.ErrorText).ToString());
        }
        finally
        {
            GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void ResourceDictionaries_ContainBothThemeBrushes()
    {
        var dicts = GuiPalette.BuildResourceDictionaries();
        dicts.Should().ContainKey(GuiPalette.GuiThemeVariant.Dark);
        dicts.Should().ContainKey(GuiPalette.GuiThemeVariant.Light);

        var light = dicts[GuiPalette.GuiThemeVariant.Light];
        light.ContainsKey("GuiWindowBackground").Should().BeTrue();
        var brush = light["GuiWindowBackground"].Should().BeAssignableTo<ISolidColorBrush>().Subject;
        brush.Color.ToString().Should().Be(Color.Parse("#f5f5f5").ToString());
    }

    [Fact]
    public void ToggleThemeVm_FlipsIsDarkTheme()
    {
        // InMemory store → ConfigurationService 走内存文件系统，LoadThemeFromSettings 读到 Auto
        // 提前返回，不会异步覆盖 IsDarkTheme（B8：裸构造读真实 settings.json 的 theme 键导致偶发翻转）
        var vm = new MainViewModel(null, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        vm.IsDarkTheme.Should().BeTrue();

        vm.ToggleThemeCommand.Execute(null);
        vm.IsDarkTheme.Should().BeFalse();

        vm.ToggleThemeCommand.Execute(null);
        vm.IsDarkTheme.Should().BeTrue();
    }
}