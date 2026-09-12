namespace JoinCode.Abstractions.UI;

/// <summary>
/// UI 主题种类 — 对齐 TS ThemePicker.tsx themeOptions (7个固定主题)
///
/// 主题值映射:
/// - Auto: 跟随终端默认主题
/// - Dark/Light: 基础明暗主题
/// - DarkDaltonized/LightDaltonized: 色盲友好主题
/// - DarkAnsi/LightAnsi: 仅 ANSI 颜色主题(兼容性回退)
///
/// 使用说明:
/// - 通过 <see cref="ThemeKindExtensions.ToValue"/> 获取字符串
/// - 通过 <see cref="ThemeKindExtensions.FromValue"/> 解析字符串
/// - 通过 <see cref="ThemeKindExtensions.IsDefined"/> 验证枚举值
/// </summary>
public enum ThemeKind
{
    /// <summary>跟随终端默认主题</summary>
    [EnumValue("auto")] Auto,

    /// <summary>深色模式</summary>
    [EnumValue("dark")] Dark,

    /// <summary>浅色模式</summary>
    [EnumValue("light")] Light,

    /// <summary>深色模式(色盲友好)</summary>
    [EnumValue("dark-daltonized")] DarkDaltonized,

    /// <summary>浅色模式(色盲友好)</summary>
    [EnumValue("light-daltonized")] LightDaltonized,

    /// <summary>深色模式(仅 ANSI 颜色)</summary>
    [EnumValue("dark-ansi")] DarkAnsi,

    /// <summary>浅色模式(仅 ANSI 颜色)</summary>
    [EnumValue("light-ansi")] LightAnsi,
}
