namespace NativeTui.Core;

/// <summary>
/// 主题选择 — 对齐基准 utils/theme.ts THEME_NAMES + getTheme（名称 → 色板解析）+ 引擎侧
/// ThemeCommand 七名清单（auto 收编 — S11 遗留#3 双通道裁决：主题名集合双通道一致）。
/// 名称常量唯一数据源（消费方禁硬编码字符串）；精确匹配对齐基准 switch（大小写敏感）。
/// auto 解析唯一数据源：<see cref="ResolveAutoName"/>（6-18 点 light 其余 dark — 引擎
/// ThemeCommand.ApplyTheme 与 Host LoadThemeNameAsync 的重复解析收编于此）。
/// P3（本批不做）：light-daltonized / dark-daltonized / light-ansi / dark-ansi 四套色板
/// 未落地，Resolve 命中时暂回退 Dark（Names 清单已含全名 — /theme 补全列表展示用），
/// 落地后在 Resolve 对应分支替换为色板属性即可。
/// </summary>
public static class ThemeSelection
{
    /// <summary>自动主题名（按本地时段解析 light/dark — 引擎侧 auto 语义对齐）。</summary>
    public const string AutoName = "auto";

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

    /// <summary>主题名清单（对齐引擎侧 ThemeCommand 七名序：auto 首位）— /theme 补全列表与配置校验消费。</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        AutoName,
        DarkName,
        LightName,
        LightDaltonizedName,
        DarkDaltonizedName,
        LightAnsiName,
        DarkAnsiName,
    ];

    /// <summary>
    /// auto 时段解析 — 6-18 点（含 6 不含 18）light，其余 dark。
    /// 唯一数据源（引擎 ApplyTheme / Host LoadThemeNameAsync 同规则收编 — 消费方禁重写小时判定）。
    /// </summary>
    /// <param name="hour">本地小时（0-23）。</param>
    /// <returns>解析后的主题名（light/dark）。</returns>
    public static string ResolveAutoName(int hour) =>
        hour is >= 6 and < 18 ? LightName : DarkName;

    /// <summary>
    /// 名称 → 色板（对齐基准 getTheme switch）。精确匹配（大小写敏感）；
    /// auto → 按当前本地时段解析（<see cref="ResolveAutoName"/>）；
    /// daltonized/ansi 名命中但色板 P3 未实现 → 暂回退 Dark；
    /// null/空/未知名/大小写不匹配 → Dark（基准 default 分支语义）。
    /// </summary>
    /// <param name="name">主题名（null 或未知名回退 Dark）。</param>
    /// <returns>解析出的色板实例（静态单例，无分配）。</returns>
    public static Theme Resolve(string? name) =>
        Resolve(name, DateTime.Now.Hour);

    /// <summary>
    /// 名称 → 色板（小时注入重载 — auto 分支确定性测试；其余分支不消费 hour）。
    /// </summary>
    /// <param name="name">主题名。</param>
    /// <param name="hour">本地小时（0-23 — 仅 auto 分支消费）。</param>
    /// <returns>解析出的色板实例。</returns>
    public static Theme Resolve(string? name, int hour) =>
        name switch
        {
            AutoName => Resolve(ResolveAutoName(hour), hour),
            LightName => Theme.Light,
            // P3：daltonized/ansi 四套色板落地后，此处分支改返回对应色板
            LightDaltonizedName or DarkDaltonizedName or LightAnsiName or DarkAnsiName => Theme.Dark,
            _ => Theme.Dark,
        };
}
