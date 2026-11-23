namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 冷却服务 — 统一的"首次触发，冷却内不触发，冷却后恢复触发"机制。
/// 实例类，线程安全（ConcurrentDictionary + CAS TryAdd/TryUpdate）。
/// 实例方法 TryTrigger 供新代码使用（DI 注入独立实例，测试隔离）。
/// 静态方法 ShouldTrigger/RecordTrigger/Reset/Cleanup 委托给 Instance，向后兼容旧代码。
/// </summary>
public sealed class CooldownService {
    private readonly ConcurrentDictionary<string, DateTime> _lastTrigger = new();
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 全局单例 — 向后兼容旧代码的静态访问
    /// </summary>
    public static readonly CooldownService Instance = new();

    /// <summary>
    /// 尝试触发 — 原子操作：首次返回 true 并记录，冷却内返回 false，冷却后返回 true 并更新。
    /// 合并 ShouldTrigger + RecordTrigger 为单个 CAS 操作，消除两步之间的竞态。
    /// </summary>
    /// <param name="key">去重键</param>
    /// <param name="cooldown">冷却时长，默认5分钟</param>
    /// <returns>true 表示已触发（首次或冷却过期）；false 表示在冷却期内</returns>
    public bool TryTrigger(string key, TimeSpan? cooldown = null) {
        var cd = cooldown ?? DefaultCooldown;
        var now = DateTime.UtcNow;

        while (true) {
            if (_lastTrigger.TryGetValue(key, out var last)) {
                if (now - last < cd) return false;
                if (_lastTrigger.TryUpdate(key, now, last)) return true;
                continue;
            }
            if (_lastTrigger.TryAdd(key, now)) return true;
        }
    }

    // ── 静态方法委托给 Instance，向后兼容旧代码 ──

    /// <summary>静态向后兼容：是否应该触发</summary>
    public static bool ShouldTrigger(string key, TimeSpan? cooldown = null) {
        var cd = cooldown ?? DefaultCooldown;
        if (Instance._lastTrigger.TryGetValue(key, out var last)) {
            return DateTime.UtcNow - last >= cd;
        }
        return true;
    }

    /// <summary>静态向后兼容：记录已触发</summary>
    public static void RecordTrigger(string key) {
        Instance._lastTrigger[key] = DateTime.UtcNow;
    }

    /// <summary>静态向后兼容：重置全部冷却状态（仅供测试）</summary>
    public static void Reset() => Instance._lastTrigger.Clear();

    /// <summary>静态向后兼容：清理过期记录</summary>
    public static void Cleanup(TimeSpan? maxAge = null) {
        var threshold = DateTime.UtcNow - (maxAge ?? TimeSpan.FromHours(1));
        foreach (var kvp in Instance._lastTrigger) {
            if (kvp.Value < threshold) {
                Instance._lastTrigger.TryRemove(kvp.Key, out _);
            }
        }
    }
}
