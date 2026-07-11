namespace JoinCode.Abstractions.Configuration.Settings;

/// <summary>
/// 配置来源枚举 — 对齐 TS 版 SETTING_SOURCES
/// 优先级从低到高: UserSettings → ProjectSettings → LocalSettings → FlagSettings → PolicySettings
/// 合并方向: 从低优先级到高优先级依次 Merge，后者覆盖前者
///
/// 写入目标: GlobalConfig → ~/.jcc/global.json, 其余 → ~/.jcc/settings.json
/// </summary>
public enum SettingSource
{
    /// <summary>
    /// 全局配置: ~/.jcc/global.json — 对齐 TS ~/.claude.json
    /// 存储: 主题、编辑器模式、通知偏好等跨项目全局设置
    /// </summary>
    [EnumValue("global")] GlobalConfig = -1,

    /// <summary>
    /// 用户全局设置: ~/.jcc/settings.json
    /// </summary>
    [EnumValue("userSettings")] UserSettings = 0,

    /// <summary>
    /// 项目共享设置: .jcc/settings.json（可提交到 Git）
    /// </summary>
    [EnumValue("projectSettings")] ProjectSettings = 1,

    /// <summary>
    /// 项目本地设置: .jcc/settings.local.json（gitignored，个人偏好）
    /// </summary>
    [EnumValue("localSettings")] LocalSettings = 2,

    /// <summary>
    /// CLI 标志设置: --settings 参数指定的路径
    /// </summary>
    [EnumValue("flagSettings")] FlagSettings = 3,

    /// <summary>
    /// 策略设置: managed-settings.json / MDM / 远程策略（管理员强制）
    /// </summary>
    [EnumValue("policySettings")] PolicySettings = 4,
}
