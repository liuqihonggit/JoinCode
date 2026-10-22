namespace NativeTui.Core;

/// <summary>
/// 主题选择 — 对齐基准 utils/theme.ts THEME_NAMES（6 套清单）+ getTheme（名称 → 色板解析）。
/// 名称常量唯一数据源（消费方禁硬编码字符串）；精确匹配对齐基准 switch（大小写敏感）。
/// P3（本批不做）：light-daltonized / dark-daltonized / light-ansi / dark-ansi 四套色板
/// 未落地，Resolve 命中时暂回退 Dark（Names 清单已含全 6 名 — /theme 补全列表展示用），
/// 落地后在 Resolve 对应分支替换为色板属性即可。
/// 接线（未决问题）：ReplShell 硬编码 Theme.Dark 无切换入口、Host /theme 命令注册。
/// </summary>
public static class ThemeSelection
{
    /// <summary>暗色主题名（默认）。</summary>
    public const string DarkName = "dark";

    /// <summary>亮色主题名。</summary>
    public const string LightName = "light";

    /// <summary>亮色色盲友好主题名（P3 色板未实现）。</summary>
    public const string LightDaltonizedName = "light-daltonized";

    /// <summary>暗色色盲友好主题名（P3 色板未实现）。</summary>
    public const string DarkDaltonizedName = "dark-daltonized";

    /// <summary>亮色 ANSI 16 色主题名（P3 色板未实现）。</summary>
    public const string LightAnsiName = "light-ansi";

    /// <summary>暗色 ANSI 16 色主题名（P3 色板未实现）。</summary>
    public const string DarkAnsiName = "dark-ansi";

    /// <summary>主题名清单（对齐基准 THEME_NAMES 声明序）— /theme 补全列表与配置校验消费。</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        DarkName,
        LightName,
        LightDaltonizedName,
        DarkDaltonizedName,
        LightAnsiName,
        DarkAnsiName,
    ];

    /// <summary>
    /// 名称 → 色板（对齐基准 getTheme switch）。精确匹配（大小写敏感）；
    /// daltonized/ansi 名命中但色板 P3 未实现 → 暂回退 Dark；
    /// null/空/未知名/大小写不匹配 → Dark（基准 default 分支语义）。
    /// </summary>
    /// <param name="name">主题名（null 或未知名回退 Dark）。</param>
    /// <returns>解析出的色板实例（静态单例，无分配）。</returns>
    public static Theme Resolve(string? name) =>
        name switch
        {
            LightName => Theme.Light,
            // P3：daltonized/ansi 四套色板落地后，此处分支改返回对应色板
            LightDaltonizedName or DarkDaltonizedName or LightAnsiName or DarkAnsiName => Theme.Dark,
            _ => Theme.Dark,
        };
}
