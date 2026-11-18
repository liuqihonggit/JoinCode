namespace JoinCode.Abstractions.Configuration.Settings;

/// <summary>
/// 配置键的唯一清单 —— 一个键的全部事实（键名、类型、描述、可选值、存储源、文件内路径、
/// 运行时同步键、读写钩子）只在这里声明一次。
/// <para><b>为什么合并</b>：在此之前同一个键的事实散在四张表里 ——
/// <c>ConfigKey</c> 枚举（15 个键名 + 一句描述）、<c>SupportedSettings</c>（16 个键的类型/描述/选项）、
/// <c>SettingSourceMap</c>（键 → 哪个文件）、<c>SettingPathMap</c>（键 → 文件内哪个字段）。
/// 四张表各有各的增删节奏，于是同一个键能出现三种说法：
/// <c>editorMode</c> 一处写「default/vim」，另两处写「vim/emacs/default」（<c>emacs</c> 根本没有实现）；
/// <c>apiKey</c> 在枚举里登记成可配置项，<c>settings.json</c> 里压根没有这个字段。
/// 更实在的代价是：<c>profile</c>/<c>fastMode</c>/<c>effortLevel</c>/<c>outputStyle</c> 只在枚举里有，
/// 于是 <c>/config set</c> 能写、<c>config_set</c> 却报「Unknown setting」—— 用户和 LLM 各看见一半。</para>
/// <para><b>表外的键</b>：<see cref="SourceOf"/> 回落 <see cref="SettingSource.UserSettings"/>、
/// <see cref="PathOf"/> 返回 <c>null</c>（按扁平键 <c>current.&lt;key&gt;</c> 处理）——
/// 与既有行为一致，避免把「表外键」变成硬失败。</para>
/// <para><b>为什么在契约层</b>：工具 exe 受依赖墙只能引契约层；规则留在上层就必然再长出第二套
/// （与参数名归一化、bash 风险分级同一条理由）。</para>
/// </summary>
public static class ConfigKeyRegistry {
    /// <summary>当前供应商占位符 —— 运行时替换为 <c>settings.json</c> 的 <c>current.profile</c>。</summary>
    public const string ProfilePlaceholder = "{profile}";

    /// <summary>全部已声明的设置键（键名 → 定义）。顺序即 <c>config_list</c> 的展示顺序。</summary>
    private static readonly FrozenDictionary<string, ConfigSetting> Settings = Build();

    /// <summary>全部已声明的设置键（键名 → 定义）。</summary>
    public static IReadOnlyDictionary<string, ConfigSetting> All => Settings;

    /// <summary>取设置键的定义；表外键返回 <c>null</c>。</summary>
    /// <param name="key">设置键名（存储键，非枚举名）。</param>
    /// <returns>键定义，或 <c>null</c>。</returns>
    public static ConfigSetting? Get(string key) =>
        key is not null && Settings.TryGetValue(key, out var setting) ? setting : null;

    /// <summary>该键是否是受支持的配置项（表内）。</summary>
    /// <param name="key">设置键名。</param>
    /// <returns>受支持返回 true。</returns>
    public static bool IsSupported(string key) => key is not null && Settings.ContainsKey(key);

    /// <summary>
    /// 取设置键的存储源 —— 表外键回落 <see cref="SettingSource.UserSettings"/>。
    /// </summary>
    /// <param name="key">设置键名。</param>
    /// <returns>该键的存储源。</returns>
    public static SettingSource SourceOf(string key) =>
        Get(key)?.Source ?? SettingSource.UserSettings;

    /// <summary>
    /// 取设置键在文件内的 JSON 路径段；表外键返回 <c>null</c>（表示扁平键 <c>current.&lt;key&gt;</c>）。
    /// </summary>
    /// <param name="key">设置键名。</param>
    /// <returns>路径段数组，或 <c>null</c>。</returns>
    public static string[]? PathOf(string key) => Get(key)?.Path;

    /// <summary>该键是否声明了嵌套路径（需要按路径读写而非扁平键）。</summary>
    /// <param name="key">设置键名。</param>
    /// <returns>声明了返回 true。</returns>
    public static bool IsNested(string key) => Get(key)?.Path is not null;

    /// <summary>取设置键的可选值列表（静态选项优先，其次动态选项）；无选项返回 <c>null</c>。</summary>
    /// <param name="key">设置键名。</param>
    /// <returns>可选值数组，或 <c>null</c>。</returns>
    public static string[]? OptionsOf(string key) {
        var setting = Get(key);
        if (setting is null)
            return null;
        if (setting.Options is { Length: > 0 })
            return [.. setting.Options];
        return setting.GetOptions?.Invoke();
    }

    /// <summary>
    /// 给一个（很可能是打错的）键名推荐最接近的已知键。
    /// <para>放在这里是为了让「斜杠命令」与「config 工具」给出同一份建议 ——
    /// 两处各写一套模糊匹配，就会出现同一个错别字在 CLI 里提示 A、在模型那里提示 B。</para>
    /// </summary>
    /// <param name="key">用户输入的键名。</param>
    /// <param name="max">最多返回几个。</param>
    /// <returns>按相似度降序的候选键；无候选时为空数组。</returns>
    public static string[] Suggest(string key, int max = 3) {
        if (string.IsNullOrWhiteSpace(key) || max <= 0)
            return [];

        // 整串包含、以及按点分段匹配（permissions.mode ↔ permissions.defaultMode 是典型错法：
        // 两者互不包含，只有段名 mode 能对上）
        var segments = key.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s.Length >= 3)
            .ToArray();

        return [.. Settings.Keys
            .Select(known => (Key: known, Score: Score(known, key, segments)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Take(max)
            .Select(x => x.Key)];
    }

    private static int Score(string known, string key, string[] segments) {
        if (string.Equals(known, key, StringComparison.OrdinalIgnoreCase))
            return int.MaxValue;
        if (known.Contains(key, StringComparison.OrdinalIgnoreCase)
            || key.Contains(known, StringComparison.OrdinalIgnoreCase))
            return segments.Length + 1;
        return segments.Count(s => known.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    private static FrozenDictionary<string, ConfigSetting> Build() {
        var settings = new Dictionary<string, ConfigSetting>(StringComparer.Ordinal) {
            // ── global.json：跨项目全局偏好（对齐 TS ~/.claude.json）──
            [ConfigKeys.Theme] = new() {
                Key = ConfigKeys.Theme,
                Type = "string",
                Description = "UI 颜色主题",
                Options = ["dark", "light", "auto", "dark-daltonized", "light-daltonized", "dark-ansi", "light-ansi"],
                Source = SettingSource.GlobalConfig,
            },
            [ConfigKeys.EditorMode] = new() {
                Key = ConfigKeys.EditorMode,
                Type = "string",
                Description = "按键绑定模式",
                Options = ["default", "vim"],
                Source = SettingSource.GlobalConfig,
            },
            [ConfigKeys.DebugLog] = new() {
                Key = ConfigKeys.DebugLog,
                Type = "boolean",
                Description = "显示详细调试输出",
                Source = SettingSource.GlobalConfig,
                AppStateKey = "debuglog",
            },
            [ConfigKeys.PreferredNotifChannel] = new() {
                Key = ConfigKeys.PreferredNotifChannel,
                Type = "string",
                Description = "首选通知渠道",
                Options = ["terminal", "desktop", "disabled"],
                Source = SettingSource.GlobalConfig,
            },
            [ConfigKeys.AutoCompactEnabled] = new() {
                Key = ConfigKeys.AutoCompactEnabled,
                Type = "boolean",
                Description = "上下文满时自动压缩",
                Source = SettingSource.GlobalConfig,
            },
            [ConfigKeys.FileCheckpointingEnabled] = new() {
                Key = ConfigKeys.FileCheckpointingEnabled,
                Type = "boolean",
                Description = "启用文件检查点以便代码回退",
                Source = SettingSource.GlobalConfig,
            },
            [ConfigKeys.ShowTurnDuration] = new() {
                Key = ConfigKeys.ShowTurnDuration,
                Type = "boolean",
                Description = "响应后显示轮次耗时",
                Source = SettingSource.GlobalConfig,
            },
            [ConfigKeys.TerminalProgressBarEnabled] = new() {
                Key = ConfigKeys.TerminalProgressBarEnabled,
                Type = "boolean",
                Description = "在支持的终端中显示 OSC 9;4 进度指示器",
                Source = SettingSource.GlobalConfig,
            },
            [ConfigKeys.TodoFeatureEnabled] = new() {
                Key = ConfigKeys.TodoFeatureEnabled,
                Type = "boolean",
                Description = "启用待办/任务跟踪",
                Source = SettingSource.GlobalConfig,
            },
            [ConfigKeys.TeammateMode] = new() {
                Key = ConfigKeys.TeammateMode,
                Type = "string",
                Description = "如何生成队友: tmux 传统模式, in-process 同进程模式, auto 自动选择",
                Options = ["tmux", "in-process", "auto"],
                Source = SettingSource.GlobalConfig,
            },

            // ── settings.json：项目相关的运行期偏好（对齐 TS）──
            [ConfigKeys.Profile] = new() {
                Key = ConfigKeys.Profile,
                Type = "string",
                Description = "当前供应商预设名（指向 settings.json 的 vendor 字典，可用 /vendor 查看候选）",
                Source = SettingSource.UserSettings,
            },
            [ConfigKeys.Model] = new() {
                Key = ConfigKeys.Model,
                Type = "string",
                // 语义统一为「当前生效模型」：读的是运行时真正用的 vendor.{当前供应商}.model，
                // 写的也是它 —— 不再出现「写 current.model、运行时读 vendor.model」的两套答案（TASK090）
                // 不设静态白名单：原本写死 sonnet/opus/haiku/best（从 TS 基线带过来的四个占位），
                // 结果任何真实模型 id（deepseek-v3 等）都被判「Invalid value」，config_set model 等于不可用。
                Description = "当前生效模型（存于 vendor.{当前供应商}.model，写入即生效；可用 model_search 查看候选）",
                Source = SettingSource.UserSettings,
                Path = ["vendor", ProfilePlaceholder, "model"],
                AppStateKey = "mainLoopModel",
                FormatOnRead = v => v ?? "default",
                ValidateOnWrite = ValidateModelId,
            },
            [ConfigKeys.EffortLevel] = new() {
                Key = ConfigKeys.EffortLevel,
                Type = "string",
                Description = "推理力度（思考预算分配）",
                Options = ["low", "medium", "high", "xhigh"],
                Source = SettingSource.UserSettings,
            },
            [ConfigKeys.FastMode] = new() {
                Key = ConfigKeys.FastMode,
                Type = "boolean",
                Description = "快速模式（使用更小/更快的模型）",
                Source = SettingSource.UserSettings,
            },
            [ConfigKeys.OutputStyle] = new() {
                Key = ConfigKeys.OutputStyle,
                Type = "string",
                Description = "输出风格",
                Options = ["concise", "normal", "verbose"],
                Source = SettingSource.UserSettings,
            },
            [ConfigKeys.AlwaysThinkingEnabled] = new() {
                Key = ConfigKeys.AlwaysThinkingEnabled,
                Type = "boolean",
                Description = "启用扩展思考（false 禁用）",
                Source = SettingSource.UserSettings,
                AppStateKey = "thinkingEnabled",
            },
            [ConfigKeys.AutoMemoryEnabled] = new() {
                Key = ConfigKeys.AutoMemoryEnabled,
                Type = "boolean",
                Description = "启用自动记忆",
                Source = SettingSource.UserSettings,
            },
            [ConfigKeys.AutoDreamEnabled] = new() {
                Key = ConfigKeys.AutoDreamEnabled,
                Type = "boolean",
                Description = "启用后台记忆整合",
                Source = SettingSource.UserSettings,
            },
            [ConfigKeys.PermissionsDefaultMode] = new() {
                Key = ConfigKeys.PermissionsDefaultMode,
                Type = "string",
                Description = "工具使用的默认权限模式",
                // 值是权限模式枚举的存储键。这里写成字面量而不是 PermissionMode.ToValue()，
                // 是因为 configuration 契约层不引 security 契约层（只为一串常量加一条打包依赖边不划算）；
                // 一致性由 guard.config.tests 的 ConfigKeyRegistryTests 对着枚举逐项锁定 —— 改了枚举值测试先红。
                // 刻意不列 unattended：那是无人值守长任务用的模式（ADR 0012），只能由用户从命令行开启，
                // 不给 config 工具（模型自己调用的那条路径）留入口。原有四个值与合并前一致。
                Options = ["plan", "auto", "ask", "bypass"],
                Source = SettingSource.UserSettings,
                // PermissionChecker 读的是 settings.Current.Permissions.DefaultMode —— 扁平键写了没人读
                Path = ["current", "permissions", "defaultMode"],
            },
            [ConfigKeys.Language] = new() {
                Key = ConfigKeys.Language,
                Type = "string",
                Description = "JoinCode 响应和语音听写的首选语言（如 \"japanese\", \"spanish\"）",
                Source = SettingSource.UserSettings,
            },
        };

        return settings.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static Task<(bool Valid, string? Error)> ValidateModelId(string value) {
        if (string.IsNullOrWhiteSpace(value))
            return Task.FromResult<(bool Valid, string? Error)>((false, "Model ID cannot be empty or whitespace"));

        var trimmed = value.Trim();
        if (trimmed != value)
            return Task.FromResult<(bool Valid, string? Error)>((false, "Model ID must not have leading/trailing whitespace"));

        return Task.FromResult<(bool Valid, string? Error)>((true, null));
    }
}

/// <summary>
/// 配置键名常量 —— 取代原来的 <c>ConfigKey</c> 枚举。
/// <para>枚举的唯一价值是给调用方一个不会拼错的键名；它同时也是一张「有哪些键」的清单，
/// 于是每加一个键都要记得在枚举、SupportedSettings、两张映射表里各加一次。
/// 现在键名只是 <see cref="ConfigKeyRegistry"/> 这张唯一表的索引标签：<c>ConfigKeys.Theme</c>
/// 与表里的 <c>"theme"</c> 是同一个字符串常量，编译期就能核对。</para>
/// </summary>
public static class ConfigKeys {
    /// <summary>当前供应商预设名（指向 vendor 字典的键）。</summary>
    public const string Profile = "profile";
    /// <summary>当前生效模型（存于 <c>vendor.{profile}.model</c>）。</summary>
    public const string Model = "model";
    /// <summary>UI 主题。</summary>
    public const string Theme = "theme";
    /// <summary>按键绑定模式。</summary>
    public const string EditorMode = "editorMode";
    /// <summary>调试日志输出。</summary>
    public const string DebugLog = "debuglog";
    /// <summary>首选通知渠道。</summary>
    public const string PreferredNotifChannel = "preferredNotifChannel";
    /// <summary>自动压缩上下文。</summary>
    public const string AutoCompactEnabled = "autoCompactEnabled";
    /// <summary>文件检查点。</summary>
    public const string FileCheckpointingEnabled = "fileCheckpointingEnabled";
    /// <summary>显示轮次耗时。</summary>
    public const string ShowTurnDuration = "showTurnDuration";
    /// <summary>终端进度条。</summary>
    public const string TerminalProgressBarEnabled = "terminalProgressBarEnabled";
    /// <summary>待办/任务跟踪。</summary>
    public const string TodoFeatureEnabled = "todoFeatureEnabled";
    /// <summary>队友生成模式。</summary>
    public const string TeammateMode = "teammateMode";
    /// <summary>推理力度。</summary>
    public const string EffortLevel = "effortLevel";
    /// <summary>快速模式。</summary>
    public const string FastMode = "fastMode";
    /// <summary>输出风格。</summary>
    public const string OutputStyle = "outputStyle";
    /// <summary>扩展思考。</summary>
    public const string AlwaysThinkingEnabled = "alwaysThinkingEnabled";
    /// <summary>自动记忆。</summary>
    public const string AutoMemoryEnabled = "autoMemoryEnabled";
    /// <summary>后台记忆整合。</summary>
    public const string AutoDreamEnabled = "autoDreamEnabled";
    /// <summary>默认权限模式（嵌套键 <c>current.permissions.defaultMode</c>）。</summary>
    public const string PermissionsDefaultMode = "permissions.defaultMode";
    /// <summary>首选语言。</summary>
    public const string Language = "language";
}
