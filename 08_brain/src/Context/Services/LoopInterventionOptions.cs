namespace Core.Context;

/// <summary>
/// 循环干预选项 — 配置漏斗各级别的触发阈值和干预参数
/// </summary>
[RegisterOptions]
public sealed partial class LoopInterventionOptions : ServiceEntity
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

    /// <summary>
    /// 工具调用后LLM空响应的最大连续次数 — 超过此值强制结束本轮对话
    /// 默认5：允许5次空响应后注入系统提示词催促，第6次强制结束
    /// </summary>
    public int MaxConsecutiveEmptyResponse { get; set; } = 5;

    /// <summary>
    /// Shannon 熵减检测器配置 — 含时间窗口二次确认参数
    /// </summary>
    public ShannonEntropyConfig ShannonEntropy { get; set; } = new();

    /// <summary>输出循环检测器配置</summary>
    public OutputLoopConfig OutputLoop { get; set; } = new();

    /// <summary>逻辑指纹检测器配置</summary>
    public LogicFingerprintConfig LogicFingerprint { get; set; } = new();

    /// <summary>工具调用序列检测器配置</summary>
    public ToolCallSequenceConfig ToolCallSequence { get; set; } = new();
}

/// <summary>
/// Shannon 熵减检测器配置 — 集中管理所有熵减检测参数
/// 属性提供系统默认值，检测器构造函数从本配置显式读取参数
/// </summary>
public sealed class ShannonEntropyConfig
{
    /// <summary>熵值历史窗口大小</summary>
    public int WindowSize { get; set; } = 10;

    /// <summary>连续下降轮数阈值（连续 DeclineThreshold 轮熵递减则进入 Suspected 状态）</summary>
    public int DeclineThreshold { get; set; } = 4;

    /// <summary>最小熵差阈值（相邻轮熵差需超过此值才算"下降"）</summary>
    public double MinEntropyDelta { get; set; } = 0.05;

    /// <summary>
    /// 二次确认时间窗口 — Suspected 状态下在此窗口内再次触发则确认死循环
    /// 窗口超时则复位到 Monitoring（误报消除）
    /// </summary>
    public TimeSpan ConfirmationWindow { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// 输出循环检测器配置 — 尾部子串重复检测参数
/// </summary>
public sealed class OutputLoopConfig
{
    /// <summary>检测窗口大小</summary>
    public int WindowSize { get; set; } = 2000;

    /// <summary>最小重复模式长度</summary>
    public int MinPatternLength { get; set; } = 10;

    /// <summary>最大重复模式长度</summary>
    public int MaxPatternLength { get; set; } = 500;

    /// <summary>触发所需的最少重复次数</summary>
    public int RequiredRepeats { get; set; } = 10;

    /// <summary>检查间隔（字符数）</summary>
    public int CheckInterval { get; set; } = 50;

    /// <summary>冷却期字符数</summary>
    public int CooldownChars { get; set; } = 500;
}

/// <summary>
/// 逻辑指纹检测器配置 — 前缀+后缀hash循环检测参数
/// </summary>
public sealed class LogicFingerprintConfig
{
    /// <summary>指纹前缀长度</summary>
    public int FingerprintPrefixLen { get; set; } = 200;

    /// <summary>指纹后缀长度</summary>
    public int FingerprintSuffixLen { get; set; } = 200;

    /// <summary>滑动窗口大小</summary>
    public int WindowSize { get; set; } = 5;

    /// <summary>命中阈值</summary>
    public int HitThreshold { get; set; } = 4;
}

/// <summary>
/// 工具调用序列检测器配置 — 工具名+参数指纹重复检测参数
/// </summary>
public sealed class ToolCallSequenceConfig
{
    /// <summary>滑动窗口大小</summary>
    public int WindowSize { get; set; } = 6;

    /// <summary>最小模式长度</summary>
    public int MinPatternLength { get; set; } = 3;

    /// <summary>触发所需的最少重复次数</summary>
    public int RequiredRepeats { get; set; } = 4;
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

    public LoopInterventionOptionsBuilder WithMaxConsecutiveEmptyResponse(int max)
    {
        _options.MaxConsecutiveEmptyResponse = max;
        return this;
    }

    public LoopInterventionOptions Build() => _options;
}
