namespace McpToolDispatch;

/// <summary>
/// gh 工具各阶段耗时追踪器 — Interlocked 线程安全累加,支持多 job 并行场景
/// <para>阶段: Network(网络下载) / LsmRead(LSM缓存读) / LsmWrite(LSM缓存写) / Parse(解析/构造内存数据结构)</para>
/// <para>用法: 顶层方法创建 tracker → 设置 AsyncLocal → 底层方法读取 AsyncLocal 打点 → 顶层读取 Format()</para>
/// <para>计时用 Stopwatch.GetTimestamp() 高精度计时器,Interlocked 累加 ticks,线程安全</para>
/// </summary>
internal sealed class GhTimingTracker {
    /// <summary>AsyncLocal 传播 — ExecuteGhAsync 顶层设置后,装饰器/缓存/解析层读取打点</summary>
    internal static readonly AsyncLocal<GhTimingTracker?> CurrentTimer = new();

    private long _networkTicks;
    private long _lsmReadTicks;
    private long _lsmWriteTicks;
    private long _parseTicks;
    private int _cacheHits;
    private int _cacheMisses;
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();

    /// <summary>累加网络下载耗时(ticks)</summary>
    public void AddNetwork(long ticks) => Interlocked.Add(ref _networkTicks, ticks);
    /// <summary>累加 LSM 缓存读取耗时(ticks)</summary>
    public void AddLsmRead(long ticks) => Interlocked.Add(ref _lsmReadTicks, ticks);
    /// <summary>累加 LSM 缓存写入耗时(ticks)</summary>
    public void AddLsmWrite(long ticks) => Interlocked.Add(ref _lsmWriteTicks, ticks);
    /// <summary>累加解析/构造内存数据结构耗时(ticks)</summary>
    public void AddParse(long ticks) => Interlocked.Add(ref _parseTicks, ticks);
    /// <summary>记录缓存命中次数</summary>
    public void RecordHit() => Interlocked.Increment(ref _cacheHits);
    /// <summary>记录缓存未命中次数</summary>
    public void RecordMiss() => Interlocked.Increment(ref _cacheMisses);

    /// <summary>ticks 转 毫秒</summary>
    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>格式化耗时统计 — 附加到 gh 命令输出末尾,让用户看见各阶段耗时</summary>
    public string Format() {
        var totalMs = TicksToMs(Stopwatch.GetTimestamp() - _startTimestamp);
        var netMs = TicksToMs(_networkTicks);
        var readMs = TicksToMs(_lsmReadTicks);
        var writeMs = TicksToMs(_lsmWriteTicks);
        var parseMs = TicksToMs(_parseTicks);
        return $"⏱ {totalMs:F0}ms | 网络 {netMs:F0}ms | LSM读 {readMs:F0}ms | LSM写 {writeMs:F0}ms | 解析 {parseMs:F0}ms | 缓存 命中{_cacheHits} 未命中{_cacheMisses}";
    }
}
