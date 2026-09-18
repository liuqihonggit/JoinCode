namespace JoinCode.Gui.ViewModels;

/// <summary>ThemeKind ↔ bool IsDarkTheme 双向转换 — GUI 仅暴露明暗二态</summary>
internal static class ThemeConverter
{
    /// <summary>
    /// ThemeKind → bool IsDarkTheme 映射 — auto 按时间（6-18 点 light，否则 dark），
    /// daltonized/ansi 降级为基础明暗（GUI 调色板暂不支持色盲友好变体）。
    /// </summary>
    public static bool ToIsDark(ThemeKind theme) => theme switch
    {
        ThemeKind.Dark or ThemeKind.DarkDaltonized or ThemeKind.DarkAnsi => true,
        ThemeKind.Light or ThemeKind.LightDaltonized or ThemeKind.LightAnsi => false,
        ThemeKind.Auto => DateTime.Now.Hour is < 6 or >= 18,
        _ => true
    };

    /// <summary>bool IsDarkTheme → ThemeKind 映射 — GUI 仅暴露 dark/light 二态</summary>
    public static ThemeKind FromIsDark(bool isDark) => isDark ? ThemeKind.Dark : ThemeKind.Light;
}
