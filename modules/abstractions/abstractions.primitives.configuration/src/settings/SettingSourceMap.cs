namespace JoinCode.Abstractions.Configuration.Settings;

/// <summary>
/// 设置键 → 存储源的唯一映射表 — 对齐 TS <c>src/tools/ConfigTool/supportedSettings.ts</c> 的 <c>source</c> 字段。
/// <para><b>为什么需要它</b>：同一个设置键可能被两条路径读写 ——
/// CLI 斜杠命令（<c>/theme</c>、<c>/config</c>）、GUI、以及 LLM 可见的 <c>config_get</c>/<c>config_set</c> 工具。
/// 三条路径若各自决定「这个键住哪个文件」，就会出现同一键两处答案的静默分叉
/// （TASK089：用户设的主题 LLM 读不到，LLM 设的主题用户看不到）。
/// 把归属收敛到这张表，三条路径问同一处，问题从根上消失。</para>
/// <para><b>写入目标</b>：<see cref="SettingSource.GlobalConfig"/> → <c>~/.jcc/global.json</c>（对齐 TS <c>~/.claude.json</c>），
/// <see cref="SettingSource.UserSettings"/> → <c>~/.jcc/settings.json</c> 的 <c>current</c> 分支。</para>
/// <para><b>表外的键</b>一律按 <see cref="SettingSource.UserSettings"/> 处理 ——
/// 与 TS 侧「未在 supportedSettings 声明的键不参与 config 工具」的行为一致。</para>
/// </summary>
public static class SettingSourceMap {
    /// <summary>键 → 存储源映射（键名与存储键完全一致）。</summary>
    private static readonly FrozenDictionary<string, SettingSource> Sources =
        new Dictionary<string, SettingSource>(StringComparer.Ordinal) {
            // ── global.json：跨项目全局偏好（对齐 TS）──
            ["theme"] = SettingSource.GlobalConfig,
            ["editorMode"] = SettingSource.GlobalConfig,
            ["debuglog"] = SettingSource.GlobalConfig,
            ["preferredNotifChannel"] = SettingSource.GlobalConfig,
            ["autoCompactEnabled"] = SettingSource.GlobalConfig,
            ["fileCheckpointingEnabled"] = SettingSource.GlobalConfig,
            ["showTurnDuration"] = SettingSource.GlobalConfig,
            ["terminalProgressBarEnabled"] = SettingSource.GlobalConfig,
            ["todoFeatureEnabled"] = SettingSource.GlobalConfig,
            ["teammateMode"] = SettingSource.GlobalConfig,

            // ── settings.json：项目相关的运行期偏好（对齐 TS）──
            ["model"] = SettingSource.UserSettings,
            ["alwaysThinkingEnabled"] = SettingSource.UserSettings,
            ["autoMemoryEnabled"] = SettingSource.UserSettings,
            ["autoDreamEnabled"] = SettingSource.UserSettings,
            ["permissions.defaultMode"] = SettingSource.UserSettings,
            ["language"] = SettingSource.UserSettings,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// 取设置键的存储源 — 表外键回落 <see cref="SettingSource.UserSettings"/>。
    /// </summary>
    /// <param name="key">设置键名（存储键，非枚举名）。</param>
    /// <returns>该键的存储源。</returns>
    public static SettingSource SourceOf(string key) =>
        key is not null && Sources.TryGetValue(key, out var source)
            ? source
            : SettingSource.UserSettings;

    /// <summary>该键是否有显式的存储源声明（表内）。</summary>
    /// <param name="key">设置键名。</param>
    /// <returns>表内返回 true。</returns>
    public static bool IsDeclared(string key) => key is not null && Sources.ContainsKey(key);

    /// <summary>全部已声明的键（含存储源）— 供对账脚本与 /config list 展示。</summary>
    public static IReadOnlyDictionary<string, SettingSource> All => Sources;
}
