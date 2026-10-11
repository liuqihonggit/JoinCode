namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 单工具频率限制配置 — 按工具名+时间窗口计次，超阈值触发强烈重复性警告提示。
/// </summary>
public sealed record ToolQuotaConfig {
    /// <summary>默认配置 — Bash 1分钟内20次触发强烈警告</summary>
    public static readonly ToolQuotaConfig Default = new();

    /// <summary>计次时间窗口，默认1分钟</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>窗口内调用次数阈值，达到则触发强烈重复性警告，默认20次</summary>
    public int Threshold { get; init; } = 20;

    /// <summary>替代工具提示（可选），如 "专用工具 Y"；为空时提示不含替代建议</summary>
    public string? AlternativeToolHint { get; init; }

    /// <summary>转换为频率门控配置（高频方向）</summary>
    public GateConfig ToGateConfig() => new() {
        Window = Window,
        Threshold = Threshold,
        Direction = GateDirection.HighFrequency,
    };
}

/// <summary>
/// 单工具频率限制服务 — 针对单个工具高频率使用进行计次与强烈重复性警告提示。
/// <para>
/// 设计哲学：不没收/不拒绝工具调用（那会卡住 AI），而是每次高频调用都注入强烈重复性警告提示，
/// 让 AI 自己嫌烦而主动换工具/换策略。按工具名+时间窗口计次，超阈值持续警告。
/// 全局静态状态，生命周期为整个进程。/clear 调用 Reset 重置全部配额。
/// </para>
/// <para>
/// 内部委托给 FrequencyGate（高频闹钟方向），与 LowFrequencyInductionService（低频冷却方向）共享同一套频率门控基础设施。
/// </para>
/// </summary>
public static class ToolQuotaService {
    private static readonly FrequencyGate Gate = new();

    /// <summary>
    /// 记数当前调用次数。
    /// </summary>
    public static int CountCalls(string toolName, ToolQuotaConfig? config = null) {
        var cfg = config ?? ToolQuotaConfig.Default;
        return Gate.CountInWindow(toolName, cfg.Window);
    }

    /// <summary>
    /// 记录一次调用（入队 + 清过期）。
    /// </summary>
    public static void RecordCall(string toolName, ToolQuotaConfig? config = null) {
        var cfg = config ?? ToolQuotaConfig.Default;
        Gate.Record(toolName, cfg.Window);
    }

    /// <summary>
    /// 是否应该警告 — 窗口内调用次数达到阈值时返回 true。
    /// <para>不拒绝调用，仅标记应该注入强烈重复性警告提示。</para>
    /// </summary>
    public static bool ShouldWarn(string toolName, ToolQuotaConfig? config = null) {
        var cfg = config ?? ToolQuotaConfig.Default;
        return Gate.ShouldSignal(toolName, cfg.ToGateConfig());
    }

    /// <summary>
    /// 获取强烈重复性警告提示 — 含工具名、当前次数、阈值和替代工具建议（如有）。
    /// <para>每次高频调用都注入此提示，让 AI 嫌烦主动换工具/换策略，而非强制没收。</para>
    /// </summary>
    public static string GetWarningPrompt(string toolName, ToolQuotaConfig? config = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        var cfg = config ?? ToolQuotaConfig.Default;
        var currentCount = Gate.CountInWindow(toolName, cfg.Window);
        var hint = string.IsNullOrEmpty(cfg.AlternativeToolHint)
            ? ""
            : $"，建议改用 {cfg.AlternativeToolHint}";
        return $"⚠️⚠️ 强烈提示：你已在 {(int)cfg.Window.TotalMinutes} 分钟内调用工具 {toolName} 共 {currentCount} 次（阈值 {cfg.Threshold}）{hint}。" +
               $"这非常高频，请立即考虑：1.改用替代工具 2.批量处理减少调用 3.调整策略。" +
               $"此提示将持续出现直到你降低调用频率。";
    }

    /// <summary>
    /// 获取当前所有高频工具名（窗口内调用次数达到阈值）— 供 LoopInterventionMiddleware 注入警告时读取。
    /// </summary>
    public static IReadOnlyCollection<string> GetHighFrequencyTools(ToolQuotaConfig? config = null) {
        var cfg = config ?? ToolQuotaConfig.Default;
        return Gate.GetTriggeredKeys(cfg.ToGateConfig());
    }

    /// <summary>
    /// 重置全部配额 — 仅供 /clear 调用，清除所有工具的调用记录。
    /// </summary>
    public static void Reset() => Gate.Reset();

    /// <summary>
    /// 清理过期记录 — 删除调用历史已清空的工具，防止字典无限增长。
    /// </summary>
    public static void Cleanup() => Gate.Cleanup();
}
