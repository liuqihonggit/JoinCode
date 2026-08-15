namespace JoinCode.Abstractions.Configuration.Settings;

/// <summary>
/// 核心配置键名 — 用于 /config 命令、配置服务 GetAsync/SetAsync 调用
/// 枚举值即存储键(单数据源),消费方通过 ToValue() 获取
/// 所有键对应 settings.json 的 current 分支内部字段
/// </summary>
public enum ConfigKey
{
    /// <summary>当前供应商预设名（指向 vendor 字典的键）</summary>
    [EnumValue("profile")] Profile,

    /// <summary>UI 主题 (dark/light/auto/daltonized/ansi)</summary>
    [EnumValue("theme")] Theme,

    /// <summary>按键绑定模式 (vim/emacs/default)</summary>
    [EnumValue("editorMode")] EditorMode,

    /// <summary>调试日志输出 (true/false)</summary>
    [EnumValue("debuglog")] DebugLog,

    /// <summary>自动压缩上下文 (true/false)</summary>
    [EnumValue("autoCompactEnabled")] AutoCompactEnabled,

    /// <summary>自动记忆 (true/false)</summary>
    [EnumValue("autoMemoryEnabled")] AutoMemoryEnabled,

    /// <summary>文件检查点 (true/false)</summary>
    [EnumValue("fileCheckpointingEnabled")] FileCheckpointingEnabled,

    /// <summary>显示轮次耗时 (true/false)</summary>
    [EnumValue("showTurnDuration")] ShowTurnDuration,

    /// <summary>扩展思考 (true/false)</summary>
    [EnumValue("alwaysThinkingEnabled")] AlwaysThinkingEnabled,

    /// <summary>默认权限模式 (allow/ask/deny)</summary>
    [EnumValue("permissions.defaultMode")] PermissionsDefaultMode,

    /// <summary>首选语言 (zh/en/ja/...)</summary>
    [EnumValue("language")] Language,

    /// <summary>快速模式 (true/false)</summary>
    [EnumValue("fastMode")] FastMode,

    /// <summary>推理力度 (low/medium/high/xhigh)</summary>
    [EnumValue("effortLevel")] EffortLevel,

    /// <summary>输出风格 (concise/verbose/normal)</summary>
    [EnumValue("outputStyle")] OutputStyle,

    /// <summary>API Key(明文存储,优先用 ProviderEnvVar 环境变量)</summary>
    [EnumValue("apiKey")] ApiKey,
}
