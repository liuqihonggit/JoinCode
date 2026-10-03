namespace JoinCode.Gui.Theming;

/// <summary>
/// 应用资源统一注册 —— 供真实 App 与 headless 测试 App 复用同一份配置：
/// Fluent 主题 + 语义配色 ThemeDictionaries + 转换器。避免真实/测试两处资源漂移。
/// 支持 4 主题（Dark/Light/SolarizedDark/SolarizedLight）— Solarized 通过替换 ThemeDictionaries 槽位实现。
/// </summary>
public static class GuiAppResources {
    private static ResourceDictionary? _themeHost;
    private static FluentTheme? _fluentTheme;
    private static AccentOption? _accent;
    private static ResourceDictionary? _stringHost;

    /// <summary>将 Fluent 主题、语义配色 ThemeDictionaries、全部转换器注册进应用资源。</summary>
    public static void Register(Application app) {
        _fluentTheme = new FluentTheme();
        app.Styles.Add(_fluentTheme);
        // 共享控件样式（设计语言单一数据源）：必须在 FluentTheme 之后追加以覆盖默认外观
        app.Styles.Add(new GuiControlStyles());
        app.Resources["GuiMonoFont"] = new Avalonia.Media.FontFamily("Consolas,Cascadia Mono,Menlo,monospace");
        app.Resources["GuiMotionDuration"] = TimeSpan.FromMilliseconds(160);
        app.Resources["GuiPopupShadow"] = new Avalonia.Media.BoxShadows(Avalonia.Media.BoxShadow.Parse("0 6 16 0 #90000000"));
        // 底部升起式补全面板专用：向上弥散的环境阴影（面板从输入栏背后向上滑出，阴影朝上）
        app.Resources["GuiPaletteShadowUp"] = new Avalonia.Media.BoxShadows(Avalonia.Media.BoxShadow.Parse("0 -10 28 0 #55000000"));
        _themeHost = new ResourceDictionary();
        ApplySchemeToHost(GuiPalette.GuiThemeVariant.Dark);
        app.Resources.MergedDictionaries.Add(_themeHost);
        app.Resources.MergedDictionaries.Add(BuildConverters());
        // 国际化字符串字典（默认中文）
        _stringHost = new ResourceDictionary();
        ApplyLanguage(ViewModels.LanguageKind.Zh);
        app.Resources.MergedDictionaries.Add(_stringHost);
        ApplyAccent(AppearanceCatalog.Load().Accents[0]);
    }

    /// <summary>切换界面语言 — 替换字符串资源字典槽位</summary>
    public static void ApplyLanguage(ViewModels.LanguageKind lang) {
        if (_stringHost is null)
            return;
        _stringHost.Clear();
        var strings = GetStrings(lang);
        foreach (var (key, value) in strings)
            _stringHost[key] = value;
    }

    /// <summary>获取指定语言的字符串字典 — AOT 兼容,硬编码无反射</summary>
    private static IReadOnlyDictionary<string, string> GetStrings(ViewModels.LanguageKind lang) => lang switch {
        ViewModels.LanguageKind.Zh => new Dictionary<string, string> {
            ["Menu.File"] = "文件", ["Menu.Edit"] = "编辑", ["Menu.View"] = "视图", ["Menu.Help"] = "帮助",
            ["Activity.Sessions"] = "会话列表", ["Activity.FileTree"] = "目录树", ["Activity.Editor"] = "代码编辑器",
            ["Activity.Interceptor"] = "AI 工具拦截器", ["Activity.Settings"] = "设置", ["Activity.Language"] = "中/En 切换",
            ["Input.Placeholder"] = "输入消息，Ctrl+Enter 发送 / Enter 换行…", ["Input.Send"] = "发送", ["Input.SelectModel"] = "选择模型",
            ["Stats.Title"] = "📊 会话统计", ["Stats.Messages"] = "消息数", ["Stats.Sessions"] = "会话数",
            ["Stats.Chars"] = "字符数", ["Stats.Tokens"] = "估算 Token",
            ["Empty.Title"] = "把想法变成代码", ["Empty.Subtitle"] = "探索代码 · 执行工具 · 协作完成任务",
            ["Common.NewSession"] = "＋ 新建对话", ["Common.SearchMessages"] = "搜索消息正文", ["Common.Statistics"] = "📊 统计"
        },
        ViewModels.LanguageKind.En => new Dictionary<string, string> {
            ["Menu.File"] = "File", ["Menu.Edit"] = "Edit", ["Menu.View"] = "View", ["Menu.Help"] = "Help",
            ["Activity.Sessions"] = "Sessions", ["Activity.FileTree"] = "File Tree", ["Activity.Editor"] = "Code Editor",
            ["Activity.Interceptor"] = "AI Tool Interceptor", ["Activity.Settings"] = "Settings", ["Activity.Language"] = "Zh/En Toggle",
            ["Input.Placeholder"] = "Type a message, Ctrl+Enter to send / Enter for newline…", ["Input.Send"] = "Send", ["Input.SelectModel"] = "Select Model",
            ["Stats.Title"] = "📊 Session Stats", ["Stats.Messages"] = "Messages", ["Stats.Sessions"] = "Sessions",
            ["Stats.Chars"] = "Characters", ["Stats.Tokens"] = "Est. Tokens",
            ["Empty.Title"] = "Turn ideas into code", ["Empty.Subtitle"] = "Explore code · Run tools · Collaborate",
            ["Common.NewSession"] = "＋ New Chat", ["Common.SearchMessages"] = "Search messages", ["Common.Statistics"] = "📊 Stats"
        },
        _ => new Dictionary<string, string>()
    };

    /// <summary>应用配置驱动的强调色，保留消息角色和安全状态的语义颜色。</summary>
    public static void ApplyAccent(AccentOption accent) {
        _accent = accent;
        if (_fluentTheme is not null) {
            var color = Color.Parse(accent.Fill);
            _fluentTheme.Palettes[ThemeVariant.Dark] = new Avalonia.Themes.Fluent.ColorPaletteResources { Accent = color };
            _fluentTheme.Palettes[ThemeVariant.Light] = new Avalonia.Themes.Fluent.ColorPaletteResources { Accent = color };
        }
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
            ["BoolToThinkingOpacity"] = new Converters.BoolToThinkingOpacityConverter(),
            ["StringEquals"] = Converters.StringEqualsConverter.Instance,
            ["EffortToDouble"] = Converters.EffortLevelToDoubleConverter.Instance,
            ["EffortToBrush"] = Converters.EffortIndexToBrushConverter.Instance,
            ["EffortToLabel"] = Converters.EffortIndexToLabelConverter.Instance
        };
        return dict;
    }
}
