namespace JoinCode.Gui.Theming;

/// <summary>
/// 配色渲染器（ADR 0145 Core/Host 拆分点）：消费 Core <see cref="GuiPalette"/> 的纯数据配色方案，
/// 构建 Avalonia <c>ResourceDictionary</c> 与画刷/对比度工具。渲染面独占本类，数据面零 Avalonia。
/// </summary>
public static class GuiPaletteRenderer {
    /// <summary>以"主题 → Brush 资源字典"的形式生成 ThemeDictionaries，供 Application 注入。</summary>
    public static IReadOnlyDictionary<GuiPalette.GuiThemeVariant, ResourceDictionary> BuildResourceDictionaries() {
        var result = new Dictionary<GuiPalette.GuiThemeVariant, ResourceDictionary> {
            [GuiPalette.GuiThemeVariant.Dark] = BuildDictionary(GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Dark)),
            [GuiPalette.GuiThemeVariant.Light] = BuildDictionary(GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.Light)),
            [GuiPalette.GuiThemeVariant.SolarizedDark] = BuildDictionary(GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.SolarizedDark)),
            [GuiPalette.GuiThemeVariant.SolarizedLight] = BuildDictionary(GuiPalette.SchemeFor(GuiPalette.GuiThemeVariant.SolarizedLight))
        };
        return result;
    }

    /// <summary>构建指定主题的资源字典（供 GuiAppResources 主题切换时替换 ThemeDictionaries 槽位）。</summary>
    public static ResourceDictionary BuildDictionaryFor(GuiPalette.GuiThemeVariant variant)
        => BuildDictionary(GuiPalette.SchemeFor(variant));

    private static ResourceDictionary BuildDictionary(GuiPalette.Scheme scheme) {
        var dict = new ResourceDictionary();
        foreach (var (key, value) in GuiPalette.SemanticTuples(scheme))
            dict[key] = ToBrush(value);
        return dict;
    }

    /// <summary>解析十六进制色为不可变画刷（供资源和转换器共用）。</summary>
    public static ISolidColorBrush ToBrush(string hex)
        => new SolidColorBrush(Color.Parse(hex));

    /// <summary>计算两色 WCAG 相对亮度。</summary>
    public static double RelativeLuminance(Color c) {
        double L(double v) => v <= 0.03928
            ? v / 12.92
            : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * L(c.R / 255.0) + 0.7152 * L(c.G / 255.0) + 0.0722 * L(c.B / 255.0);
    }

    /// <summary>计算两色对比度（1 到 21）。</summary>
    public static double ContrastRatio(Color a, Color b) {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }
}
