namespace JoinCode.Abstractions.Configuration.Settings;

/// <summary>
/// 配置设置元数据 —— 一个键的全部事实：键名、类型、描述、可选值、存储源、文件内路径、
/// 运行时同步键、读写钩子。对齐 TS: supportedSettings.ts SettingConfig。
/// <para><b>存储源与路径也在这里</b>：它们原先分别住在 <c>SettingSourceMap</c> 与 <c>SettingPathMap</c> 两张独立表里，
/// 与类型/描述分家，于是同一个键的增删要在三处同步，漏一处就出现「写得进读不出」。现在只有唯一清单
/// <see cref="ConfigKeyRegistry"/>，每行自洽。</para>
/// </summary>
public sealed class ConfigSetting {
    /// <summary>配置键名（存储键，非枚举名）。</summary>
    public required string Key { get; init; }

    /// <summary>获取配置值类型（"boolean" | "string"）。</summary>
    public required string Type { get; init; } // "boolean" | "string"
    /// <summary>获取配置描述。</summary>
    public required string Description { get; init; }

    /// <summary>
    /// 存储源 —— 决定这个键写进 <c>global.json</c> 还是 <c>settings.json</c>。
    /// 三条读写路径（斜杠命令、GUI、config 工具）都问这一处，不再各自判断。
    /// </summary>
    public SettingSource Source { get; init; } = SettingSource.UserSettings;

    /// <summary>
    /// 文件内 JSON 路径段（从文件根写全，`{profile}` 运行期替换）。
    /// <c>null</c> 表示扁平键 <c>current.&lt;key&gt;</c>。
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
