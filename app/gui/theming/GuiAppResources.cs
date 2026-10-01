namespace JoinCode.Gui.Theming;

/// <summary>
/// 应用资源统一注册 —— 供真实 App 与 headless 测试 App 复用同一份配置：
/// Fluent 主题 + 语义配色 ThemeDictionaries + 转换器。避免真实/测试两处资源漂移。
/// 支持 4 主题（Dark/Light/SolarizedDark/SolarizedLight）— Solarized 通过替换 ThemeDictionaries 槽位实现。
/// </summary>
public static class GuiAppResources {
    private static ResourceDictionary? _themeHost;
    private static AccentOption? _accent;

    /// <summary>将 Fluent 主题、语义配色 ThemeDictionaries、全部转换器注册进应用资源。</summary>
    public static void Register(Application app) {
        app.Styles.Add(new FluentTheme());
        // 共享控件样式（设计语言单一数据源）：必须在 FluentTheme 之后追加以覆盖默认外观
        app.Styles.Add(new GuiControlStyles());
        app.Resources["GuiMonoFont"] = new Avalonia.Media.FontFamily("Consolas,Cascadia Mono,Menlo,monospace");
        app.Resources["GuiPopupShadow"] = new Avalonia.Media.BoxShadows(Avalonia.Media.BoxShadow.Parse("0 6 16 0 #90000000"));
        // 底部升起式补全面板专用：向上弥散的环境阴影（面板从输入栏背后向上滑出，阴影朝上）
        app.Resources["GuiPaletteShadowUp"] = new Avalonia.Media.BoxShadows(Avalonia.Media.BoxShadow.Parse("0 -10 28 0 #55000000"));
        _themeHost = new ResourceDictionary();
        ApplySchemeToHost(GuiPalette.GuiThemeVariant.Dark);
        app.Resources.MergedDictionaries.Add(_themeHost);
        app.Resources.MergedDictionaries.Add(BuildConverters());
        ApplyAccent(AppearanceCatalog.Load().Accents[0]);
    }

    /// <summary>应用配置驱动的强调色，保留消息角色和安全状态的语义颜色。</summary>
    public static void ApplyAccent(AccentOption accent) {
        _accent = accent;
        ApplySchemeToHost(GuiPalette.CurrentVariant);
    }

    /// <summary>
    /// 切换主题 — 更新 ThemeDictionaries 槽位为指定主题的配色。
    /// Solarized 主题复用 Dark/Light 槽位（FluentTheme 内置控件用基础明暗，自定义语义色用 Solarized 配色）。
    /// </summary>
    public static void ApplyTheme(GuiPalette.GuiThemeVariant variant) {
        if (_themeHost is null) return;
        ApplySchemeToHost(variant);
    }

    private static void ApplySchemeToHost(GuiPalette.GuiThemeVariant variant) {
        var avaVariant = variant is GuiPalette.GuiThemeVariant.Light or GuiPalette.GuiThemeVariant.SolarizedLight
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        var dictionary = GuiPalette.BuildDictionaryFor(variant);
        if (_accent is not null) {
            var light = avaVariant == ThemeVariant.Light;
            dictionary["GuiAccentText"] = GuiPalette.ToBrush(light ? _accent.LightText : _accent.DarkText);
            dictionary["GuiAccentSubtle"] = GuiPalette.ToBrush(light ? _accent.LightSubtle : _accent.DarkSubtle);
            dictionary["GuiAccentSubtleHover"] = GuiPalette.ToBrush(light ? _accent.LightSubtle : _accent.DarkSubtle);
            dictionary["GuiAccentFill"] = GuiPalette.ToBrush(_accent.Fill);
            dictionary["GuiAccentHover"] = GuiPalette.ToBrush(_accent.Fill);
            dictionary["GuiSessionHighlight"] = GuiPalette.ToBrush(light ? _accent.LightSubtle : _accent.DarkSubtle);
        }
        _themeHost!.ThemeDictionaries[avaVariant] = dictionary;
    }

    /// <summary>构建转换器资源字典（键名必须与 App.axaml 原声明一致，供 XAML {StaticResource} 解析）。</summary>
    private static ResourceDictionary BuildConverters() {
        var dict = new ResourceDictionary {
            ["BoolToRoleBrush"] = new Converters.BoolToRoleBrushConverter(),
            ["MsgBarBrush"] = new Converters.MsgBarBrushConverter(),
            ["BoolToSessHighlight"] = new Converters.BoolToSessionHighlightConverter(),
            ["StatusToBrush"] = new Converters.StatusToBrushConverter(),
            ["BoolToWarnBrush"] = new Converters.BoolToWarnBrushConverter(),
            ["BoolToThinkingOpacity"] = new Converters.BoolToThinkingOpacityConverter()
        };
        return dict;
    }
}
