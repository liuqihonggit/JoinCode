using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace JoinCode.Gui.Theming;

/// <summary>
/// 应用资源统一注册 —— 供真实 App 与 headless 测试 App 复用同一份配置：
/// Fluent 主题 + 语义配色 ThemeDictionaries + 转换器。避免真实/测试两处资源漂移。
/// </summary>
public static class GuiAppResources
{
    /// <summary>将 Fluent 主题、语义配色 ThemeDictionaries、全部转换器注册进应用资源。</summary>
    public static void Register(Application app)
    {
        app.Styles.Add(new FluentTheme());
        app.Resources["GuiMonoFont"] = new Avalonia.Media.FontFamily("Consolas,Cascadia Mono,Menlo,monospace");
        app.Resources["GuiPopupShadow"] = new Avalonia.Media.BoxShadows(Avalonia.Media.BoxShadow.Parse("0 6 16 0 #90000000"));
        var themeHost = new ResourceDictionary();
        foreach (var (variant, scheme) in GuiPalette.BuildResourceDictionaries())
        {
            var avaVariant = variant == GuiPalette.GuiThemeVariant.Light
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
            themeHost.ThemeDictionaries[avaVariant] = scheme;
        }
        app.Resources.MergedDictionaries.Add(themeHost);
        app.Resources.MergedDictionaries.Add(BuildConverters());
    }

    /// <summary>构建转换器资源字典（键名必须与 App.axaml 原声明一致，供 XAML {StaticResource} 解析）。</summary>
    private static ResourceDictionary BuildConverters()
    {
        var dict = new ResourceDictionary
        {
            ["BoolToRoleBrush"] = new Converters.BoolToRoleBrushConverter(),
            ["BoolToSessHighlight"] = new Converters.BoolToSessionHighlightConverter(),
            ["StatusToBrush"] = new Converters.StatusToBrushConverter(),
            ["BoolToWarnBrush"] = new Converters.BoolToWarnBrushConverter(),
            ["BoolToThinkingOpacity"] = new Converters.BoolToThinkingOpacityConverter()
        };
        return dict;
    }
}