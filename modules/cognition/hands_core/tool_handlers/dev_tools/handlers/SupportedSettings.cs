// JCC11004 抑制: 存量返回 null 集合, 后续改为空集合
#pragma warning disable JCC11004
namespace Core.Configuration;

/// <summary>
/// 支持的配置设置注册表。
/// 对齐 TS: supportedSettings.ts SUPPORTED_SETTINGS
/// <para><b>存储源不在本表声明</b> —— 统一由契约层 <see cref="SettingSourceMap"/> 决定
/// （对齐 TS supportedSettings.ts 的 source 字段）。本表只声明「这个键支持吗、类型、选项、校验」。
/// 曾经在这里手写 <c>Source = "global"</c>，而 CLI 斜杠命令与 GUI 走的是另一套判断，
/// 于是同一个键在两个文件里各有一份值，用户与 LLM 互相看不见对方的设置（TASK089）。</para>
/// </summary>
public static class SupportedSettings {
    /// <summary>
    /// 所有支持的配置设置字典 — 键为设置名，值为配置定义
    /// </summary>
    public static FrozenDictionary<string, ConfigSetting> All { get; }

    static SupportedSettings() {
        var settings = new Dictionary<string, ConfigSetting>(StringComparer.Ordinal) {
            ["theme"] = new() {
                Type = "string",
                Description = "UI 颜色主题",
                Options = ["dark", "light", "auto", "dark-daltonized", "light-daltonized", "dark-ansi", "light-ansi"],
            },
            ["editorMode"] = new() {
                Type = "string",
                Description = "按键绑定模式",
                Options = ["default", "vim"],
            },
            ["debuglog"] = new() {
                Type = "boolean",
                Description = "显示详细调试输出",
                AppStateKey = "debuglog",
            },
            ["preferredNotifChannel"] = new() {
                Type = "string",
                Description = "首选通知渠道",
                Options = ["terminal", "desktop", "disabled"],
            },
            ["autoCompactEnabled"] = new() {
                Type = "boolean",
                Description = "上下文满时自动压缩",
            },
            ["fileCheckpointingEnabled"] = new() {
                Type = "boolean",
                Description = "启用文件检查点以便代码回退",
            },
            ["showTurnDuration"] = new() {
                Type = "boolean",
                Description = "响应后显示轮次耗时",
            },
            ["terminalProgressBarEnabled"] = new() {
                Type = "boolean",
                Description = "在支持的终端中显示 OSC 9;4 进度指示器",
            },
            ["todoFeatureEnabled"] = new() {
                Type = "boolean",
                Description = "启用待办/任务跟踪",
            },
            ["teammateMode"] = new() {
                Type = "string",
                Description = "如何生成队友: tmux 传统模式, in-process 同进程模式, auto 自动选择",
                Options = ["tmux", "in-process", "auto"],
            },
            ["model"] = new() {
                Type = "string",
                // 语义统一为「当前生效模型」：读的是运行时真正用的 vendor.{profile}.model，
                // 写的也是它 —— 不再出现「写 current.model、运行时读 vendor.model」的两套答案（TASK090）
                // 不设静态白名单：原本写死 sonnet/opus/haiku/best（从 TS 基线带过来的四个占位），
                // 结果任何真实模型 id（deepseek-v3 等）都被判「Invalid value」，config_set model 等于不可用。
                Description = "当前生效模型（存于 vendor.{当前供应商}.model，写入即生效；可用 model_search 查看候选）",
                AppStateKey = "mainLoopModel",
                FormatOnRead = v => v ?? "default",
                ValidateOnWrite = ValidateModelId,
            },
            ["alwaysThinkingEnabled"] = new() {
                Type = "boolean",
                Description = "启用扩展思考（false 禁用）",
                AppStateKey = "thinkingEnabled",
            },
            ["autoMemoryEnabled"] = new() {
                Type = "boolean",
                Description = "启用自动记忆",
            },
            ["autoDreamEnabled"] = new() {
                Type = "boolean",
                Description = "启用后台记忆整合",
            },
            ["permissions.defaultMode"] = new() {
                Type = "string",
                Description = "工具使用的默认权限模式",
                Options = [PermissionMode.Plan.ToValue(), PermissionMode.Auto.ToValue(), PermissionMode.Ask.ToValue(), PermissionMode.Bypass.ToValue()],
            },
            ["language"] = new() {
                Type = "string",
                Description = "JoinCode 响应和语音听写的首选语言（如 \"japanese\", \"spanish\"）",
            },
        };

        All = settings.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// 取设置键的存储源 — 委托契约层 <see cref="SettingSourceMap"/>（唯一数据源）。
    /// </summary>
    /// <param name="key">设置键名。</param>
    /// <returns>该键的存储源。</returns>
    public static SettingSource SourceOf(string key) => SettingSourceMap.SourceOf(key);

    /// <summary>检查指定设置键是否受支持</summary>
    /// <param name="key">设置键名</param>
    /// <returns>受支持返回 true，否则返回 false</returns>
    public static bool IsSupported(string key) => All.ContainsKey(key);

    /// <summary>获取指定设置键的配置定义</summary>
    /// <param name="key">设置键名</param>
    /// <returns>配置定义；键不存在时返回 null</returns>
    public static ConfigSetting? GetConfig(string key) => All.GetValueOrDefault(key);

    /// <summary>获取所有受支持的设置键名</summary>
    /// <returns>设置键名数组</returns>
    public static string[] GetAllKeys() => [.. All.Keys];

    /// <summary>获取指定设置的可选值列表</summary>
    /// <param name="key">设置键名</param>
    /// <returns>可选值数组；设置不存在或无可选值时返回 null</returns>
    public static string[]? GetOptionsForSetting(string key) {
        if (!All.TryGetValue(key, out var config))
            return null;

        if (config.Options is not null)
            return [.. config.Options];

        if (config.GetOptions is not null)
            return config.GetOptions();

        return null;
    }

    /// <summary>
    /// 取设置键在 JSON 里的路径段 — 委托契约层 <see cref="SettingPathMap"/>（唯一数据源）。
    /// <para>原先是本文件里 <c>ConfigSetting.Path</c> 的字段，但那个字段<b>零消费方</b>：
    /// <c>config_set permissions.defaultMode</c> 照旧写扁平键，运行时读的却是嵌套键，改权限模式不生效。</para>
    /// </summary>
    /// <param name="key">设置键名</param>
    /// <returns>路径段数组；扁平键返回按 '.' 分割的结果</returns>
    public static string[] GetPath(string key) => SettingPathMap.PathOf(key) ?? key.Split('.');

    private static Task<(bool Valid, string? Error)> ValidateModelId(string value) {
        if (string.IsNullOrWhiteSpace(value))
            return Task.FromResult<(bool Valid, string? Error)>((false, "Model ID cannot be empty or whitespace"));

        var trimmed = value.Trim();
        if (trimmed != value)
            return Task.FromResult<(bool Valid, string? Error)>((false, "Model ID must not have leading/trailing whitespace"));

        return Task.FromResult<(bool Valid, string? Error)>((true, null));
    }
}