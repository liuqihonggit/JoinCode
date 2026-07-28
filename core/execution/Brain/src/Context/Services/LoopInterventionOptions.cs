namespace Core.Context;

/// <summary>
/// 循环干预选项 — 配置漏斗各级别的触发阈值和干预参数
/// </summary>
[RegisterOptions]
public sealed partial class LoopInterventionOptions
{
    public int HardTruncateThreshold { get; set; } = 3;
    public int CompactThreshold { get; set; } = 5;
    public int MaxRetryAttempts { get; set; } = 2;
    public float RetryTemperature { get; set; } = 0.6f;
    public string SoftIntervenePrompt { get; set; } = "\n\n[系统提示：检测到输出可能陷入循环，请用序号→箭头方式总结当前回答再继续推理。]\n\n";
    public string HardTruncatePrompt { get; set; } = "\n\n⚠️ 检测到循环输出，已自动截断。";
    public string CompactPrompt { get; set; } = "\n\n⚠️ 多次重连仍检测到循环，正在压缩上下文...";
    public string CompactSuccessPrompt { get; set; } = "\n\n上下文已压缩，请继续。";
    public string CompactFallbackPrompt { get; set; } = "\n\n上下文已重置，请重新描述你的需求。";
    public ContextFoldDecision CompactFoldDecision { get; set; } = ContextFoldDecision.FoldAggressive;

    /// <summary>
    /// 任务推进时的触发次数折扣 — 如果任务有推进，有效触发次数 = 实际触发次数 - 折扣值
    /// 默认1：有推进时漏斗级别降一级（如 Level 2 → Level 1）
    /// </summary>
    public int ProgressDiscount { get; set; } = 1;

    /// <summary>
    /// Level 2 重连全部失败后的降温重试温度 — 给模型最后一次低温机会打破循环
    /// 默认0.3：比 RetryTemperature(0.6) 更低，大幅降低重复同一思路的概率
    /// </summary>
    public float SecondChanceTemperature { get; set; } = 0.3f;

    /// <summary>
    /// Level 2 撤回后是否在历史中插入审计标记 — 防止用户回顾时逻辑断裂
    /// </summary>
    public bool InsertRewindAuditMark { get; set; } = true;

    /// <summary>
    /// Level 3 重置前是否保留最近1轮用户消息作为种子 — 避免完全丢失用户需求
    /// </summary>
    public bool PreserveLastUserMessageOnReset { get; set; } = true;
}

public sealed class LoopInterventionOptionsBuilder
{
    private readonly LoopInterventionOptions _options;

    private LoopInterventionOptionsBuilder()
    {
        _options = new LoopInterventionOptions();
    }

    public static LoopInterventionOptionsBuilder Create() => new();

    public LoopInterventionOptionsBuilder WithHardTruncateThreshold(int threshold)
    {
        _options.HardTruncateThreshold = threshold;
        return this;
    }

    public LoopInterventionOptionsBuilder WithCompactThreshold(int threshold)
    {
        _options.CompactThreshold = threshold;
        return this;
    }

    public LoopInterventionOptionsBuilder WithMaxRetryAttempts(int attempts)
    {
        _options.MaxRetryAttempts = attempts;
        return this;
    }

    public LoopInterventionOptionsBuilder WithRetryTemperature(float temperature)
    {
        _options.RetryTemperature = temperature;
        return this;
    }

    public LoopInterventionOptionsBuilder WithSoftIntervenePrompt(string prompt)
    {
        _options.SoftIntervenePrompt = prompt;
        return this;
    }

    public LoopInterventionOptionsBuilder WithCompactFoldDecision(ContextFoldDecision decision)
    {
        _options.CompactFoldDecision = decision;
        return this;
    }

    public LoopInterventionOptionsBuilder WithProgressDiscount(int discount)
    {
        _options.ProgressDiscount = discount;
        return this;
    }

    public LoopInterventionOptionsBuilder WithSecondChanceTemperature(float temperature)
    {
        _options.SecondChanceTemperature = temperature;
        return this;
    }

    public LoopInterventionOptionsBuilder WithInsertRewindAuditMark(bool enable)
    {
        _options.InsertRewindAuditMark = enable;
        return this;
    }

    public LoopInterventionOptionsBuilder WithPreserveLastUserMessageOnReset(bool enable)
    {
        _options.PreserveLastUserMessageOnReset = enable;
        return this;
    }

    public LoopInterventionOptions Build() => _options;
}
