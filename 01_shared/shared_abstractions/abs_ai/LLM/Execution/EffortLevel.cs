namespace JoinCode.Abstractions.LLM;

/// <summary>
/// 推理力度级别 — 对齐 TS EffortLevel
/// </summary>
public enum EffortLevel
{
    [EnumValue("low")] Low,
    [EnumValue("medium")] Medium,
    [EnumValue("high")] High,
    [EnumValue("max")] Max,
    [EnumValue("auto")] Auto
}

/// <summary>
/// EffortLevel 别名解析 — 处理 xhigh→Max, unset/default→Auto
/// </summary>
public static class EffortLevelHelper
{
    /// <summary>
    /// 从字符串解析 EffortLevel，支持别名: xhigh→Max, unset/default→Auto
    /// </summary>
    public static EffortLevel? ParseEffortLevel(string? value)
    {
        if (value is null) return null;
        var result = EffortLevelExtensions.FromValue(value);
        if (result is not null) return result;
        return value.Equals("xhigh", StringComparison.OrdinalIgnoreCase)
            ? EffortLevel.Max
            : value is "unset" or "default"
                ? EffortLevel.Auto
                : null;
    }

    /// <summary>
    /// 数字快捷键解析: 1→Low, 2→Medium, 3→High, 4→Max
    /// </summary>
    public static EffortLevel? ParseNumericAlias(string? value)
    {
        return value switch
        {
            "1" => EffortLevel.Low,
            "2" => EffortLevel.Medium,
            "3" => EffortLevel.High,
            "4" => EffortLevel.Max,
            _ => null
        };
    }
}
