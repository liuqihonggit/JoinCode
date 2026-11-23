namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 信号方向 — 高频触发（闹钟）或低频触发（冷却）。两者互为对称。
/// </summary>
public enum GateDirection {
    /// <summary>高频触发 — 窗口内事件次数 >= 阈值时信号（闹钟：太频繁了）</summary>
    HighFrequency,

    /// <summary>低频触发 — 距上次事件 >= 窗口时信号（冷却：太久没动了）</summary>
    LowFrequency,
}

/// <summary>
/// 频率门控配置 — 统一高频闹钟和低频冷却的参数。
/// </summary>
public sealed record GateConfig {
    /// <summary>默认高频配置 — 1分钟内20次触发</summary>
    public static readonly GateConfig DefaultHigh = new() { Direction = GateDirection.HighFrequency };

    /// <summary>默认低频配置 — 距上次10分钟触发</summary>
    public static readonly GateConfig DefaultLow = new() {
        Direction = GateDirection.LowFrequency,
        Window = TimeSpan.FromMinutes(10),
    };

    /// <summary>时间窗口。高频: 计数窗口; 低频: 距上次的间隔</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>次数阈值。高频: 窗口内次数达此值触发; 低频: 忽略（仅需距上次 >= Window）</summary>
    public int Threshold { get; init; } = 20;

    /// <summary>信号方向 — 高频触发或低频触发</summary>
    public GateDirection Direction { get; init; } = GateDirection.HighFrequency;
}

/// <summary>
/// 频率门控 — 统一高频闹钟和低频冷却的对称逻辑。
/// <para>
/// 高频方向（闹钟）: 窗口内 Record 次数 >= Threshold → ShouldSignal=true（"太频繁了，换工具"）
/// 低频方向（冷却）: 距上次 Record >= Window → ShouldSignal=true（"太久了，该行动了"）
/// 两者互为对称：一个管"太高"，一个管"太低"，共用同一套时间窗口计数基础设施。
/// </para>
/// <para>
/// 线程安全：ConcurrentDictionary + ConcurrentQueue。全局状态需 /clear 调 Reset 重置。
/// </para>
/// </summary>
public sealed class FrequencyGate {
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _events = new();

    /// <summary>
    /// 记数窗口内事件次数 — 清过期后返回队列长度。
    /// </summary>
    /// <param name="key">事件键</param>
    /// <param name="window">时间窗口</param>
    /// <returns>窗口内事件次数</returns>
    public int CountInWindow(string key, TimeSpan window) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_events.TryGetValue(key, out var queue))
            return 0;
        var cutoff = DateTime.UtcNow - window;
        while (queue.TryPeek(out var ts) && ts < cutoff)
            queue.TryDequeue(out _);
        return queue.Count;
    }

    /// <summary>
    /// 距上次事件的时间 — 无事件返回 TimeSpan.MaxValue。
    /// </summary>
    /// <param name="key">事件键</param>
    /// <returns>距上次事件的时间跨度</returns>
    public TimeSpan TimeSinceLast(string key) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_events.TryGetValue(key, out var queue) || queue.IsEmpty)
            return TimeSpan.MaxValue;
        queue.TryPeek(out var oldest);
        return DateTime.UtcNow - oldest;
    }

    /// <summary>
    /// 记录一次事件 — 入队 + 清过期。
    /// </summary>
    /// <param name="key">事件键</param>
    /// <param name="window">时间窗口（可选，用于清过期，默认1分钟）</param>
    public void Record(string key, TimeSpan? window = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var w = window ?? TimeSpan.FromMinutes(1);
        var queue = _events.GetOrAdd(key, _ => new ConcurrentQueue<DateTime>());
        var now = DateTime.UtcNow;
        queue.Enqueue(now);
        var cutoff = now - w;
        while (queue.TryPeek(out var ts) && ts < cutoff)
            queue.TryDequeue(out _);
    }

    /// <summary>
    /// 是否应该触发信号 — 按方向判断。
    /// <para>高频: CountInWindow >= Threshold</para>
    /// <para>低频: TimeSinceLast >= Window（无事件也触发）</para>
    /// </summary>
    /// <param name="key">事件键</param>
    /// <param name="config">门控配置</param>
    /// <returns>true 表示应该触发信号</returns>
    public bool ShouldSignal(string key, GateConfig config) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(config);
        return config.Direction switch {
            GateDirection.HighFrequency => CountInWindow(key, config.Window) >= config.Threshold,
            GateDirection.LowFrequency => TimeSinceLast(key) >= config.Window,
            _ => false,
        };
    }

    /// <summary>
    /// 获取所有应触发信号的键 — 供批量检查时读取。
    /// </summary>
    /// <param name="config">门控配置</param>
    /// <returns>应触发信号的键集合</returns>
    public IReadOnlyCollection<string> GetTriggeredKeys(GateConfig config) {
        ArgumentNullException.ThrowIfNull(config);
        var result = new List<string>();
        foreach (var key in _events.Keys) {
            if (ShouldSignal(key, config))
                result.Add(key);
        }
        return result;
    }

    /// <summary>
    /// 重置全部状态 — 清除所有事件记录。
    /// </summary>
    public void Reset() => _events.Clear();

    /// <summary>
    /// 清理空记录 — 删除事件队列已清空的键，防止字典无限增长。
    /// </summary>
    public void Cleanup() {
        foreach (var kvp in _events) {
            if (kvp.Value.IsEmpty)
                _events.TryRemove(kvp.Key, out _);
        }
    }
}
