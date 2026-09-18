namespace JoinCode.Cli.Output;

/// <summary>
/// 配置优先级枚举 — 对齐架构指南四级优先级
/// Flag(4) > Env(3) > Config(2) > Default(1)
/// </summary>
public enum ConfigPriority
{
    /// <summary>默认值 — 最低优先级</summary>
    [EnumValue("default")]
    Default = 1,

    /// <summary>配置文件 — ~/.jcc/settings.json 等</summary>
    [EnumValue("config")]
    Config = 2,

    /// <summary>环境变量 — JCC_VENDOR/JCC_MODEL_ID 等</summary>
    [EnumValue("env")]
    Env = 3,

    /// <summary>命令行参数 — --model/--permission-mode 等</summary>
    [EnumValue("flag")]
    Flag = 4,
}
