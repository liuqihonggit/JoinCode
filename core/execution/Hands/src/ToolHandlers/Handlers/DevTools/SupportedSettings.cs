namespace Core.Configuration;

/// <summary>
/// 支持的配置设置注册表。
/// 对齐 TS: supportedSettings.ts SUPPORTED_SETTINGS
/// </summary>
public static class SupportedSettings
{
    public static FrozenDictionary<string, ConfigSetting> All { get; }

    static SupportedSettings()
    {
        var settings = new Dictionary<string, ConfigSetting>(StringComparer.Ordinal)
        {
            ["theme"] = new()
            {
                Source = "global",
                Type = "string",
                Description = "UI 颜色主题",
                Options = ["dark", "light", "auto", "dark-daltonized", "light-daltonized", "dark-ansi", "light-ansi"],
            },
            ["editorMode"] = new()
            {
                Source = "global",
                Type = "string",
                Description = "按键绑定模式",
                Options = ["default", "vim"],
            },
            ["verbose"] = new()
            {
                Source = "global",
                Type = "boolean",
                Description = "显示详细调试输出",
                AppStateKey = "verbose",
            },
            ["preferredNotifChannel"] = new()
            {
                Source = "global",
                Type = "string",
                Description = "首选通知渠道",
                Options = ["terminal", "desktop", "disabled"],
            },
            ["autoCompactEnabled"] = new()
            {
                Source = "global",
                Type = "boolean",
                Description = "上下文满时自动压缩",
            },
            ["fileCheckpointingEnabled"] = new()
            {
                Source = "global",
                Type = "boolean",
                Description = "启用文件检查点以便代码回退",
            },
            ["showTurnDuration"] = new()
            {
                Source = "global",
                Type = "boolean",
                Description = "响应后显示轮次耗时",
            },
            ["terminalProgressBarEnabled"] = new()
            {
                Source = "global",
                Type = "boolean",
                Description = "在支持的终端中显示 OSC 9;4 进度指示器",
            },
            ["todoFeatureEnabled"] = new()
            {
                Source = "global",
                Type = "boolean",
                Description = "启用待办/任务跟踪",
            },
            ["teammateMode"] = new()
            {
                Source = "global",
                Type = "string",
                Description = "如何生成队友: tmux 传统模式, in-process 同进程模式, auto 自动选择",
                Options = ["tmux", "in-process", "auto"],
            },
            ["model"] = new()
            {
                Source = "settings",
                Type = "string",
                Description = "覆盖默认模型",
                AppStateKey = "mainLoopModel",
                GetOptions = GetModelOptions,
                FormatOnRead = v => v ?? "default",
                ValidateOnWrite = ValidateModelId,
            },
            ["alwaysThinkingEnabled"] = new()
            {
                Source = "settings",
                Type = "boolean",
                Description = "启用扩展思考（false 禁用）",
                AppStateKey = "thinkingEnabled",
            },
            ["autoMemoryEnabled"] = new()
            {
                Source = "settings",
                Type = "boolean",
                Description = "启用自动记忆",
            },
            ["autoDreamEnabled"] = new()
            {
                Source = "settings",
                Type = "boolean",
                Description = "启用后台记忆整合",
            },
            ["permissions.defaultMode"] = new()
            {
                Source = "settings",
                Type = "string",
                Description = "工具使用的默认权限模式",
                Path = ["permissions", "defaultMode"],
                Options = ["default", "plan", "acceptEdits", "dontAsk", "auto"],
            },
            ["language"] = new()
            {
                Source = "settings",
                Type = "string",
                Description = "Claude 响应和语音听写的首选语言（如 \"japanese\", \"spanish\"）",
            },
        };

        All = settings.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public static bool IsSupported(string key) => All.ContainsKey(key);

    public static ConfigSetting? GetConfig(string key) => All.GetValueOrDefault(key);

    public static string[] GetAllKeys() => [.. All.Keys];

    public static string[]? GetOptionsForSetting(string key)
    {
        if (!All.TryGetValue(key, out var config))
            return null;

        if (config.Options is not null)
            return [.. config.Options];

        if (config.GetOptions is not null)
            return config.GetOptions();

        return null;
    }

    public static string[] GetPath(string key)
    {
        if (All.TryGetValue(key, out var config) && config.Path is not null)
            return config.Path;

        return key.Split('.');
    }

    private static string[] GetModelOptions()
    {
        return ["sonnet", "opus", "haiku", "best"];
    }

    private static Task<(bool Valid, string? Error)> ValidateModelId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Task.FromResult<(bool Valid, string? Error)>((false, "Model ID cannot be empty or whitespace"));

        var trimmed = value.Trim();
        if (trimmed != value)
            return Task.FromResult<(bool Valid, string? Error)>((false, "Model ID must not have leading/trailing whitespace"));

        return Task.FromResult<(bool Valid, string? Error)>((true, null));
    }
}
