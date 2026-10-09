namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 单工具频率限制配置 — 按工具名+时间窗口计次，超阈值触发冷却期。
/// </summary>
public sealed record ToolQuotaConfig {
    /// <summary>默认配置 — Bash 1分钟内20次触发5分钟冷却</summary>
    public static readonly ToolQuotaConfig Default = new();

    /// <summary>计次时间窗口，默认1分钟</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>窗口内调用次数阈值，达到则进入冷却期，默认20次</summary>
    public int Threshold { get; init; } = 20;

    /// <summary>冷却时长，冷却期内该工具调用直接拒绝，默认5分钟</summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>替代工具提示（可选），如 "专用工具 Y"；为空时提示不含替代建议</summary>
    public string? AlternativeToolHint { get; init; }
}

/// <summary>
/// 单工具频率限制服务 — 针对单个工具高频率使用进行额度限制与没收冷却期。
/// <para>
/// 按工具名+时间窗口计次，超阈值触发冷却期。冷却期内该工具调用直接拒绝，
/// 并注入"工具 X 已冷却，建议用专用工具 Y"提示。
/// 全局静态状态，生命周期为整个进程。/clear 调用 Reset 重置全部配额。
/// </para>
/// </summary>
public static class ToolQuotaService {
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> CallHistory = new();
    private static readonly ConcurrentDictionary<string, DateTime> CooldownUntil = new();

    /// <summary>
    /// 记录一次工具调用 — 入队当前时间并清理窗口外旧记录。
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="config">配额配置（可选，默认 Default）</param>
    public static void RecordCall(string toolName, ToolQuotaConfig? config = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        var cfg = config ?? ToolQuotaConfig.Default;
        var queue = CallHistory.GetOrAdd(toolName, _ => new ConcurrentQueue<DateTime>());
        var now = DateTime.UtcNow;
        queue.Enqueue(now);

        var cutoff = now - cfg.Window;
        while (queue.TryPeek(out var ts) && ts < cutoff)
            queue.TryDequeue(out _);
    }

    /// <summary>
    /// 检查工具是否处于冷却期 — 冷却期内返回 true 表示应拒绝调用。
    /// <para>
    /// 若窗口内调用次数达到阈值，自动进入冷却期并返回 true。
    /// </para>
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="config">配额配置（可选，默认 Default）</param>
    /// <returns>true 表示在冷却期内应拒绝；false 表示可正常调用</returns>
    public static bool IsCoolingDown(string toolName, ToolQuotaConfig? config = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        var cfg = config ?? ToolQuotaConfig.Default;
        var now = DateTime.UtcNow;

        if (CooldownUntil.TryGetValue(toolName, out var until) && now < until)
            return true;

        if (!CallHistory.TryGetValue(toolName, out var queue))
            return false;

        var cutoff = now - cfg.Window;
        while (queue.TryPeek(out var ts) && ts < cutoff)
            queue.TryDequeue(out _);

        if (queue.Count < cfg.Threshold)
            return false;

        CooldownUntil[toolName] = now + cfg.Cooldown;
        return true;
    }

    /// <summary>
    /// 获取冷却提示词 — 含工具名和替代工具建议（如有）。
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="config">配额配置（可选，默认 Default）</param>
    /// <returns>冷却提示词，解释为何拒绝 + 接下来怎么做</returns>
    public static string GetCooldownPrompt(string toolName, ToolQuotaConfig? config = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        var cfg = config ?? ToolQuotaConfig.Default;
        var hint = string.IsNullOrEmpty(cfg.AlternativeToolHint)
            ? ""
            : $"，建议改用 {cfg.AlternativeToolHint}";
        var cooldownMinutes = (int)cfg.Cooldown.TotalMinutes;
        return $"⚠️ 工具 {toolName} 因高频使用（{cfg.Threshold}次/{(int)cfg.Window.TotalMinutes}分钟）已进入冷却期（{cooldownMinutes}分钟）{hint}。请稍后重试或使用替代工具。";
    }

    /// <summary>
    /// 获取当前处于冷却期的所有工具名 — 供 LoopInterventionMiddleware 第三级没收高频工具时读取。
    /// </summary>
    /// <returns>当前冷却中的工具名集合</returns>
    public static IReadOnlyCollection<string> GetCoolingTools() {
        var now = DateTime.UtcNow;
        var result = new List<string>();
        foreach (var kvp in CooldownUntil) {
            if (now < kvp.Value)
                result.Add(kvp.Key);
        }
        return result;
    }

    /// <summary>
    /// 重置全部配额 — 仅供 /clear 调用，清除所有工具的调用记录和冷却状态。
    /// </summary>
    public static void Reset() {
        CallHistory.Clear();
        CooldownUntil.Clear();
    }

    /// <summary>
    /// 清理过期记录 — 删除调用历史已清空的工具和已过期的冷却记录，防止字典无限增长。
    /// 供外部定时调用（如每 5 分钟一次），maxAge 默认 1 小时。
    /// </summary>
    /// <param name="maxAge">最大存活时长，超过此时长未再调用的工具记录将被移除，默认 1 小时</param>
    public static void Cleanup(TimeSpan? maxAge = null) {
        var threshold = DateTime.UtcNow - (maxAge ?? TimeSpan.FromHours(1));

        foreach (var kvp in CallHistory) {
            if (kvp.Value.IsEmpty)
                CallHistory.TryRemove(kvp.Key, out _);
        }

        foreach (var kvp in CooldownUntil) {
            if (kvp.Value < threshold)
                CooldownUntil.TryRemove(kvp.Key, out _);
        }
    }
}
