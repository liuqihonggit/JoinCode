namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 低频驱动诱导服务 — Agent 运行时通过低频驱动方式诱导 AI，避免高频注入造成噪声。
/// <para>
/// 按配置间隔（如每 10 分钟）检查工具使用健康度等指标，健康度下降时注入一次性诱导提示。
/// 间隔内不重复注入（去重），间隔过后方可再次诱导。全局静态状态，/clear 调用 Reset 重置。
/// </para>
/// </summary>
public static class LowFrequencyInductionService {
    private static readonly ConcurrentDictionary<string, DateTime> LastInduction = new();

    /// <summary>
    /// 是否应该诱导 — 首次返回 true，间隔内返回 false，间隔过后返回 true。
    /// </summary>
    /// <param name="eventKey">诱导事件键（如 "tool-health-check"）</param>
    /// <param name="interval">诱导间隔（如 10 分钟），间隔内不重复注入</param>
    /// <returns>true 表示应该诱导；false 表示间隔内不诱导</returns>
    public static bool ShouldInduce(string eventKey, TimeSpan interval) {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKey);
        if (LastInduction.TryGetValue(eventKey, out var last))
            return DateTime.UtcNow - last >= interval;
        return true;
    }

    /// <summary>
    /// 记录已诱导 — 更新诱导时间。
    /// </summary>
    /// <param name="eventKey">诱导事件键</param>
    public static void RecordInduction(string eventKey) {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKey);
        LastInduction[eventKey] = DateTime.UtcNow;
    }

    /// <summary>
    /// 获取低频诱导提示词 — 含事件名和健康详情（如有）。
    /// </summary>
    /// <param name="eventKey">诱导事件键</param>
    /// <param name="healthDetail">健康详情（可选），如 "工具健康度下降"</param>
    /// <returns>低频诱导提示词</returns>
    public static string GetInductionPrompt(string eventKey, string? healthDetail = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKey);
        var detail = string.IsNullOrEmpty(healthDetail) ? "" : $"（{healthDetail}）";
        return $"\n\n💡 [低频诱导] {eventKey}{detail}。请检查并调整策略。";
    }

    /// <summary>
    /// 重置全部诱导状态 — 仅供 /clear 调用，清除所有事件的诱导记录。
    /// </summary>
    public static void Reset() => LastInduction.Clear();

    /// <summary>
    /// 清理过期诱导记录 — 删除上次诱导时间超过指定时长的 key，防止 key 无限增长。
    /// 供外部定时调用（如每 30 分钟一次），maxAge 默认 2 小时。
    /// </summary>
    /// <param name="maxAge">最大存活时长，超过此时长未再诱导的 key 将被移除，默认 2 小时</param>
    public static void Cleanup(TimeSpan? maxAge = null) {
        var threshold = DateTime.UtcNow - (maxAge ?? TimeSpan.FromHours(2));
        foreach (var kvp in LastInduction) {
            if (kvp.Value < threshold)
                LastInduction.TryRemove(kvp.Key, out _);
        }
    }
}
