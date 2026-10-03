namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 冷却服务 — 统一的"首次触发，冷却内不触发，冷却后恢复触发"机制。
/// 用于软性提示去重：首次检测到违规→触发，冷却内再违规→不触发，冷却后→再次触发。
/// 全局静态状态，生命周期为整个进程。
/// </summary>
public static class CooldownService {
    private static readonly ConcurrentDictionary<string, DateTime> LastTrigger = new();
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 是否应该触发 — 首次返回 true，冷却内返回 false，冷却后返回 true。
    /// </summary>
    /// <param name="key">去重键（如 "commit-constraint"、"dynamic-sectionName"）</param>
    /// <param name="cooldown">冷却时长，默认5分钟</param>
    /// <returns>true 表示应该触发；false 表示在冷却期内，不触发</returns>
    public static bool ShouldTrigger(string key, TimeSpan? cooldown = null) {
        var cd = cooldown ?? DefaultCooldown;
        if (LastTrigger.TryGetValue(key, out var last)) {
            return DateTime.UtcNow - last >= cd;
        }
        return true;
    }

    /// <summary>
    /// 记录已触发 — 更新触发时间。
    /// </summary>
    /// <param name="key">去重键</param>
    public static void RecordTrigger(string key) {
        LastTrigger[key] = DateTime.UtcNow;
    }
}
