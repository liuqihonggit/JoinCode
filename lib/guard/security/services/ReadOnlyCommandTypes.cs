namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 标志参数类型 — 对齐 TS FlagArgType
/// </summary>
internal enum FlagArgType
{
    /// <summary>
    /// 无参数的布尔标志
    /// </summary>
    None,

    /// <summary>
    /// 需要一个参数
    /// </summary>
    Required,

    /// <summary>
    /// 参数可选
    /// </summary>
    Optional,
}

/// <summary>
/// 命令验证配置 — 对齐 TS CommandConfig
/// </summary>
internal sealed record CommandConfig(
    FrozenDictionary<string, FlagArgType> SafeFlags,
    Regex? Regex = null,
    Func<string, IReadOnlyList<string>, bool>? AdditionalDangerousCallback = null,
    bool RespectsDoubleDash = true);
