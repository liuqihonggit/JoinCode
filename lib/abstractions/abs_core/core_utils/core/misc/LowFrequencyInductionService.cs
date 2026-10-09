namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 低频驱动诱导服务 — Agent 运行时通过低频驱动方式诱导 AI，避免高频注入造成噪声。
/// <para>
/// 按配置间隔（如每 10 分钟）检查工具使用健康度等指标，健康度下降时注入一次性诱导提示。
/// 间隔内不重复注入（去重），间隔过后方可再次诱导。全局静态状态，/clear 调用 Reset 重置。
/// </para>
/// <para>
/// 内部委托给 FrequencyGate（低频冷却方向），与 ToolQuotaService（高频闹钟方向）共享同一套频率门控基础设施。
/// 两者互为对称：闹钟管"太高"，冷却管"太低"。
/// </para>
/// </summary>
public static class LowFrequencyInductionService {
    private static readonly FrequencyGate Gate = new();

    /// <summary>
    /// 是否应该诱导 — 距上次诱导 >= 间隔时返回 true，间隔内返回 false。
    /// </summary>
    /// <param name="eventKey">诱导事件键（如 "tool-health-check"）</param>
    /// <param name="interval">诱导间隔（如 10 分钟），间隔内不重复注入</param>
    /// <returns>true 表示应该诱导；false 表示间隔内不诱导</returns>
    public static bool ShouldInduce(string eventKey, TimeSpan interval) {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKey);
        return Gate.ShouldSignal(eventKey, new GateConfig {
            Window = interval,
            Direction = GateDirection.LowFrequency,
        });
    }

    /// <summary>
    /// 记录已诱导 — 更新诱导时间。
    /// </summary>
    public static void RecordInduction(string eventKey) {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKey);
        Gate.Record(eventKey);
    }

    /// <summary>
    /// 获取低频诱导提示词 — 含事件名和健康详情（如有）。
    /// </summary>
    public static string GetInductionPrompt(string eventKey, string? healthDetail = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKey);
        var detail = string.IsNullOrEmpty(healthDetail) ? "" : $"（{healthDetail}）";
        return $"\n\n💡 [低频诱导] {eventKey}{detail}。请检查并调整策略。";
    }

    /// <summary>
    /// 重置全部诱导状态 — 仅供 /clear 调用，清除所有事件的诱导记录。
    /// </summary>
    public static void Reset() => Gate.Reset();

    /// <summary>
    /// 清理过期诱导记录 — 删除上次诱导时间超过指定时长的 key，防止 key 无限增长。
    /// </summary>
    /// <param name="maxAge">最大存活时长，超过此时长未再诱导的 key 将被移除，默认 2 小时</param>
    public static void Cleanup(TimeSpan? maxAge = null) => Gate.Cleanup();
}
