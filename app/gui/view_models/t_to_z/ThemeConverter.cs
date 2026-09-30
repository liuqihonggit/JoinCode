namespace JoinCode.Gui.ViewModels;

/// <summary>ThemeKind ↔ GuiThemeVariant 双向转换 — GUI 暴露 4 主题（Dark/Light/SolarizedDark/SolarizedLight）</summary>
internal static class ThemeConverter {
    /// <summary>
    /// ThemeKind → GuiThemeVariant 映射 — auto 按时间（6-18 点 light，否则 dark），
    /// daltonized/ansi 降级为基础明暗（GUI 调色板暂不支持色盲友好变体）。
    /// </summary>
    public static GuiPalette.GuiThemeVariant ToVariant(ThemeKind theme) => theme switch {
        ThemeKind.Light or ThemeKind.LightDaltonized or ThemeKind.LightAnsi => GuiPalette.GuiThemeVariant.Light,
        ThemeKind.Auto => DateTime.Now.Hour is < 6 or >= 18 ? GuiPalette.GuiThemeVariant.Dark : GuiPalette.GuiThemeVariant.Light,
        _ => GuiPalette.GuiThemeVariant.Dark
    };

    /// <summary>GuiThemeVariant → ThemeKind 映射 — Solarized 主题持久化为 dark/light（引擎不区分 Solarized）</summary>
    public static ThemeKind FromVariant(GuiPalette.GuiThemeVariant variant) => variant switch {
        GuiPalette.GuiThemeVariant.Light or GuiPalette.GuiThemeVariant.SolarizedLight => ThemeKind.Light,
        _ => ThemeKind.Dark
    };
}