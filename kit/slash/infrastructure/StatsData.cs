namespace JoinCode.ChatCommands;

/// <summary>
/// 统计数据模型 — 对齐 TS Stats 组件数据模型
/// </summary>
public sealed class StatsData
{
    /// <summary>
    /// 总会话数
    /// </summary>
    public int TotalSessions { get; set; }

    /// <summary>
    /// 总输入 token 数
    /// </summary>
    public int TotalInputTokens { get; set; }

    /// <summary>
    /// 总输出 token 数
    /// </summary>
    public int TotalOutputTokens { get; set; }

    /// <summary>
    /// 总花费（美元）
    /// </summary>
    public decimal TotalCostUsd { get; set; }

    /// <summary>
    /// 活跃天数
    /// </summary>
    public int ActiveDays { get; set; }

    /// <summary>
    /// 最长会话时长（分钟）
    /// </summary>
    public int LongestSessionMinutes { get; set; }

    /// <summary>
    /// 按模型分组的统计明细列表
    /// </summary>
    public List<ModelStats> ModelBreakdown { get; } = [];

    /// <summary>
    /// 每日使用量列表
    /// </summary>
    public List<DailyUsage> DailyUsage { get; } = [];

    /// <summary>
    /// 统计日期范围起始（可空）
    /// </summary>
    public DateTime? DateRangeStart { get; set; }

    /// <summary>
    /// 统计日期范围结束（可空）
    /// </summary>
    public DateTime? DateRangeEnd { get; set; }

    /// <summary>
    /// 总 token 数（输入 + 输出）
    /// </summary>
    public int TotalTokens => TotalInputTokens + TotalOutputTokens;
}

/// <summary>
/// 模型统计
/// </summary>
public sealed class ModelStats
{
    /// <summary>
    /// 模型名称
    /// </summary>
    public string Model { get; }

    /// <summary>
    /// 输入 token 数
    /// </summary>
    public int InputTokens { get; }

    /// <summary>
    /// 输出 token 数
    /// </summary>
    public int OutputTokens { get; }

    /// <summary>
    /// 花费（美元）
    /// </summary>
    public decimal CostUsd { get; }

    /// <summary>
    /// 构造模型统计实例
    /// </summary>
    /// <param name="model">模型名称</param>
    /// <param name="inputTokens">输入 token 数</param>
    /// <param name="outputTokens">输出 token 数</param>
    /// <param name="costUsd">花费（美元）</param>
    public ModelStats(string model, int inputTokens, int outputTokens, decimal costUsd)
    {
        Model = model;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CostUsd = costUsd;
    }

    /// <summary>
    /// 总 token 数（输入 + 输出）
    /// </summary>
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// 每日使用量
/// </summary>
public sealed class DailyUsage
{
    /// <summary>
    /// 日期（必填，仅初始化时可设置）
    /// </summary>
    public required DateTime Date { get; init; }

    /// <summary>
    /// 输入 token 数
    /// </summary>
    public int InputTokens { get; init; }

    /// <summary>
    /// 输出 token 数
    /// </summary>
    public int OutputTokens { get; init; }

    /// <summary>
    /// 花费（美元）
    /// </summary>
    public decimal CostUsd { get; init; }

    /// <summary>
    /// 总 token 数（输入 + 输出）
    /// </summary>
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// 统计 Tab 类型
/// </summary>
public enum StatsTab
{
    /// <summary>
    /// 概览
    /// </summary>
    [EnumValue("overview")]
    Overview,

    /// <summary>
    /// 模型
    /// </summary>
    [EnumValue("models")]
    Models,

    /// <summary>
    /// 每日
    /// </summary>
    [EnumValue("daily")]
    Daily,
}
