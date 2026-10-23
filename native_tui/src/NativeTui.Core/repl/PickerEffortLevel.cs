namespace NativeTui.Core.Repl;

/// <summary>
/// 模型选择器 effort 档位（M7-6 登记遗留 / TASK066 板块 D5）— 四值枚举（引擎侧
/// JoinCode.Abstractions.LLM.EffortLevel 的 Core 投影子集，Auto 语义映射为 null
/// 由 PickerEffortConfig.InitialEffort 承载 — Core 零依赖不引引擎枚举）。
/// </summary>
public enum PickerEffortLevel : byte
{
    /// <summary>低（基准 'low' — 快速简洁）。</summary>
    Low = 0,

    /// <summary>中（基准 'medium' — 平衡）。</summary>
    Medium = 1,

    /// <summary>高（基准 'high' — 全面）。</summary>
    High = 2,

    /// <summary>最高（基准 'max' — 最深推理，仅支持 max 的模型）。</summary>
    Max = 3,
}

/// <summary>
/// PickerEffortLevel 扩展 — 符号/名称/footer 通知文本/环绕循环（基准 ModelPicker.tsx:431-442
/// cycleEffortLevel：环 = includeMax ? 四值 : 三值，当前值不在环内（如 max 后切到非支持模型）
/// clamp 到 high 索引起算，right = (idx+1)%len / left = (idx-1+len)%len 环绕）。
/// </summary>
public static class PickerEffortLevelExtensions
{
    /// <summary>全量环（支持 max 的模型 — low→medium→high→max）。</summary>
    private static readonly PickerEffortLevel[] FullCycle = [PickerEffortLevel.Low, PickerEffortLevel.Medium, PickerEffortLevel.High, PickerEffortLevel.Max];

    /// <summary>标准环（不支持 max 的模型 — low→medium→high）。</summary>
    private static readonly PickerEffortLevel[] StandardCycle = [PickerEffortLevel.Low, PickerEffortLevel.Medium, PickerEffortLevel.High];

    /// <summary>档位符号（基准 EffortLevelIndicator → effortLevelToSymbol：○/◐/●/◉）。</summary>
    public static string ToSymbol(this PickerEffortLevel level) => level switch
    {
        PickerEffortLevel.Low => Figures.EffortLow,
        PickerEffortLevel.Medium => Figures.EffortMedium,
        PickerEffortLevel.High => Figures.EffortHigh,
        PickerEffortLevel.Max => Figures.EffortMax,
        _ => Figures.EffortLow,
    };

    /// <summary>显示名（基准 capitalize(displayEffort) — "Low"/"Medium"/"High"/"Max"）。</summary>
    public static string ToDisplayName(this PickerEffortLevel level) => level switch
    {
        PickerEffortLevel.Low => "Low",
        PickerEffortLevel.Medium => "Medium",
        PickerEffortLevel.High => "High",
        PickerEffortLevel.Max => "Max",
        _ => "Low",
    };

    /// <summary>小写值名（引擎 EffortLevel.ToValue 同字面量 — 宿主持久化桥两侧一致）。</summary>
    public static string ToLowercaseName(this PickerEffortLevel level) => level switch
    {
        PickerEffortLevel.Low => "low",
        PickerEffortLevel.Medium => "medium",
        PickerEffortLevel.High => "high",
        PickerEffortLevel.Max => "max",
        _ => "low",
    };

    /// <summary>footer 通知文本（唯一数据源 — 对齐 ReplShell 种子形态 "◐ 中等 · /effort"）。</summary>
    public static string ToFooterNotificationText(this PickerEffortLevel level) =>
        $"{level.ToSymbol()} {level.ToChineseName()} · /effort";

    /// <summary>环绕循环（基准 ModelPicker.tsx:431-442 cycleEffortLevel）：
    /// 环 = includeMax ? 四值 : 三值；当前值不在环内 clamp 到 high 索引；forward 索引 +1 否则 -1，取模环绕。</summary>
    public static PickerEffortLevel Cycle(this PickerEffortLevel current, bool forward, bool includeMax)
    {
        var levels = includeMax ? FullCycle : StandardCycle;
        var idx = Array.IndexOf(levels, current);
        var currentIndex = idx >= 0 ? idx : Array.IndexOf(levels, PickerEffortLevel.High);
        return forward
            ? levels[(currentIndex + 1) % levels.Length]
            : levels[(currentIndex - 1 + levels.Length) % levels.Length];
    }

    /// <summary>中文档位名（footer 通知汉化 — 对齐种子 "中等"；Low/High/Max 依序 低/高/最高）。</summary>
    private static string ToChineseName(this PickerEffortLevel level) => level switch
    {
        PickerEffortLevel.Low => "低",
        PickerEffortLevel.Medium => "中等",
        PickerEffortLevel.High => "高",
        PickerEffortLevel.Max => "最高",
        _ => "低",
    };
}
