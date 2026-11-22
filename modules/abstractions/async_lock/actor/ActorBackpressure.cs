namespace Core.Utils;

/// <summary>
/// Actor 背压配置 — 有界通道容量 + 满时策略 + 水位线告警 + 发送超时。
/// <para>Coding Agent 场景:LLM/编译慢,消息会堆积,需有界但容量大,不丢任务。</para>
/// <para>水位线分两档:High(容量*0.8)触发生产者降速,Critical(容量*0.95)触发告警。</para>
/// <para>Capacity:有界通道容量(0=无界,负数抛 ArgumentOutOfRangeException)</para>
/// <para>FullMode:通道满时策略(默认 Wait 阻塞生产者)</para>
/// <para>HighWatermark:高水位线(null=容量*0.8)</para>
/// <para>CriticalWatermark:危险水位线(null=容量*0.95)</para>
/// <para>SendTimeout:发送超时(null=不超时,无限等待)</para>
/// <para>MaxRetries:背压重试最大次数(默认16,16次仍失败触发SendFailed事件)</para>
/// <para>RetryQueueCapacity:重试队列容量(默认1024,满时重试回写失败触发SendFailed)</para>
/// </summary>
public sealed record ActorBackpressure {
    /// <summary>有界通道容量(0=无界)</summary>
    public int Capacity { get; init; }

    /// <summary>通道满时策略</summary>
    public BoundedChannelFullMode FullMode { get; init; } = BoundedChannelFullMode.Wait;

    /// <summary>高水位线(null=容量*0.8)</summary>
    public int? HighWatermark { get; init; }

    /// <summary>危险水位线(null=容量*0.95)</summary>
    public int? CriticalWatermark { get; init; }

    /// <summary>发送超时(null=不超时,无限等待)</summary>
    public TimeSpan? SendTimeout { get; init; }

    /// <summary>背压重试最大次数(默认16)</summary>
    public int MaxRetries { get; init; } = 16;

    /// <summary>重试队列容量(默认1024)</summary>
    public int RetryQueueCapacity { get; init; } = 1024;

    /// <summary>构造 Actor 背压配置 — Capacity 负数抛 <see cref="ArgumentOutOfRangeException"/></summary>
    public ActorBackpressure(
        int Capacity,
        BoundedChannelFullMode FullMode = BoundedChannelFullMode.Wait,
        int? HighWatermark = null,
        int? CriticalWatermark = null,
        TimeSpan? SendTimeout = null,
        int MaxRetries = 16,
        int RetryQueueCapacity = 1024) {
        if (Capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(Capacity), Capacity, "Capacity 不能为负数(0=无界,正数=有界通道容量)");
        if (MaxRetries <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxRetries), MaxRetries, "MaxRetries 必须 > 0");
        if (RetryQueueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(RetryQueueCapacity), RetryQueueCapacity, "RetryQueueCapacity 必须 > 0");
        if (HighWatermark.HasValue && (HighWatermark.Value < 0 || HighWatermark.Value > Capacity))
            throw new ArgumentOutOfRangeException(nameof(HighWatermark), HighWatermark.Value, "HighWatermark 必须 >= 0 且 <= Capacity");
        if (CriticalWatermark.HasValue && (CriticalWatermark.Value < 0 || CriticalWatermark.Value > Capacity))
            throw new ArgumentOutOfRangeException(nameof(CriticalWatermark), CriticalWatermark.Value, "CriticalWatermark 必须 >= 0 且 <= Capacity");

        this.Capacity = Capacity;
        this.FullMode = FullMode;
        this.HighWatermark = HighWatermark;
        this.CriticalWatermark = CriticalWatermark;
        this.SendTimeout = SendTimeout;
        this.MaxRetries = MaxRetries;
        this.RetryQueueCapacity = RetryQueueCapacity;

        // 水位线倒置检查:仅当用户显式设置两个水位线时才校验 High < Critical
        // (默认值 null 时由 Effective 计算,整数截断可能使小容量的 High==Critical,属计算精度而非用户错误,不抛)
        if (HighWatermark.HasValue && CriticalWatermark.HasValue && HighWatermark.Value >= CriticalWatermark.Value)
            throw new ArgumentException("HighWatermark 必须 < CriticalWatermark", nameof(HighWatermark));
    }

    /// <summary>高水位线 — null 时取容量*0.8</summary>
    public int EffectiveHighWatermark => HighWatermark ?? (int)(Capacity * 0.8);

    /// <summary>危险水位线 — null 时取容量*0.95</summary>
    public int EffectiveCriticalWatermark => CriticalWatermark ?? (int)(Capacity * 0.95);

    /// <summary>Coding Agent 任务队列 — 容量 2000 + Wait + 30s 超时(用户可提交大量任务,不丢)</summary>
    public static readonly ActorBackpressure CodingAgentTask = new(
        Capacity: 2000,
        FullMode: BoundedChannelFullMode.Wait,
        HighWatermark: 1600,
        CriticalWatermark: 1900,
        SendTimeout: TimeSpan.FromSeconds(30));

    /// <summary>LLM Gateway — 容量 200 + Wait + 60s 超时(LLM 是瓶颈,200 缓冲约 30-60 分钟任务量)</summary>
    public static readonly ActorBackpressure LlmGateway = new(
        Capacity: 200,
        FullMode: BoundedChannelFullMode.Wait,
        HighWatermark: 160,
        CriticalWatermark: 190,
        SendTimeout: TimeSpan.FromSeconds(60));

    /// <summary>Router → Worker 分发 — 容量 1000 + Wait + 10s 超时(分发快,10s 超时说明 Worker 全卡死)</summary>
    public static readonly ActorBackpressure Router = new(
        Capacity: 1000,
        FullMode: BoundedChannelFullMode.Wait,
        HighWatermark: 800,
        CriticalWatermark: 950,
        SendTimeout: TimeSpan.FromSeconds(10));

    /// <summary>编译队列 — 容量 100 + Wait + 60s 超时(编译慢但串行,100 足够多仓库并发)</summary>
    public static readonly ActorBackpressure Build = new(
        Capacity: 100,
        FullMode: BoundedChannelFullMode.Wait,
        HighWatermark: 80,
        CriticalWatermark: 95,
        SendTimeout: TimeSpan.FromSeconds(60));
}

/// <summary>背压水位等级 — [EnumValue] 由 EnumMetadataGenerator 自动生成映射</summary>
public enum WatermarkLevel {
    /// <summary>正常(低于高水位线)</summary>
    [EnumValue("normal")] Normal,

    /// <summary>高水位(达到高水位线,生产者应降速)</summary>
    [EnumValue("high")] High,

    /// <summary>危险水位(达到危险水位线,即将满)</summary>
    [EnumValue("critical")] Critical
}

/// <summary>背压水位事件参数</summary>
public sealed record BackpressureEventArgs(
    string ActorId,
    int CurrentCount,
    int Capacity,
    WatermarkLevel Level);