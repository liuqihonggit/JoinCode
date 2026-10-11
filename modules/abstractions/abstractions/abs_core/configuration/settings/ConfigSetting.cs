namespace JoinCode.Abstractions.Configuration.Settings;

/// <summary>
/// 配置设置元数据。
/// 对齐 TS: supportedSettings.ts SettingConfig
/// </summary>
public sealed class ConfigSetting {
    /// <summary>获取配置值类型（"boolean" | "string"）。</summary>
    public required string Type { get; init; } // "boolean" | "string"
    /// <summary>获取配置描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取配置来源（"global" | "settings"）。</summary>
    public required string Source { get; init; } // "global" | "settings"

    /// <summary>
    /// 嵌套路径（如 permissions.defaultMode → ["permissions", "defaultMode"]）
    /// 对齐 TS: path
    /// </summary>
    public string[]? Path { get; init; }

    /// <summary>
    /// 静态选项列表
    /// 对齐 TS: options
    /// </summary>
    public string[]? Options { get; init; }

    /// <summary>
    /// 动态选项获取函数（如 model 选项根据 API 可用性动态获取）
    /// 对齐 TS: getOptions
    /// </summary>
    public Func<string[]>? GetOptions { get; init; }

    /// <summary>
    /// 热更新同步键 — 对齐 TS: appStateKey
    /// 写入成功后同步到对应 AppState 字段
    /// </summary>
    public string? AppStateKey { get; init; }

    /// <summary>
    /// 写入时异步验证
    /// 对齐 TS: validateOnWrite
    /// </summary>
    public Func<string, Task<(bool Valid, string? Error)>>? ValidateOnWrite { get; init; }

    /// <summary>
    /// 读取时格式化
    /// 对齐 TS: formatOnRead
    /// </summary>
    public Func<string?, string?>? FormatOnRead { get; init; }
}

/// <summary>
/// 模型选项
/// </summary>
public sealed class ModelOption {
    /// <summary>获取选项值。</summary>
    public string? Value { get; init; }
    /// <summary>获取选项描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取面向模型的描述。</summary>
    public string? DescriptionForModel { get; init; }
}