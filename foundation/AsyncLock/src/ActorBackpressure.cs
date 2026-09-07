namespace Core.Utils;

/// <summary>
/// Actor 背压配置 — 有界通道容量 + 满时策略 + 水位线告警 + 发送超时。
/// <para>Coding Agent 场景:LLM/编译慢,消息会堆积,需有界但容量大,不丢任务。</para>
/// <para>水位线分两档:High(容量*0.8)触发生产者降速,Critical(容量*0.95)触发告警。</para>
/// </summary>
/// <param name="Capacity">有界通道容量(0=无界)</param>
/// <param name="FullMode">通道满时策略(默认 Wait 阻塞生产者)</param>
/// <param name="HighWatermark">高水位线(null=容量*0.8)</param>
/// <param name="CriticalWatermark">危险水位线(null=容量*0.95)</param>
/// <param name="SendTimeout">发送超时(null=不超时,无限等待)</param>
public sealed record ActorBackpressure(
    int Capacity,
    BoundedChannelFullMode FullMode = BoundedChannelFullMode.Wait,
    int? HighWatermark = null,
    int? CriticalWatermark = null,
    TimeSpan? SendTimeout = null)
{
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

/// <summary>背压水位等级</summary>
public enum WatermarkLevel
{
    /// <summary>正常(低于高水位线)</summary>
    Normal,

    /// <summary>高水位(达到高水位线,生产者应降速)</summary>
    High,

    /// <summary>危险水位(达到危险水位线,即将满)</summary>
    Critical
}

/// <summary>背压水位事件参数</summary>
public sealed record BackpressureEventArgs(
    string ActorId,
    int CurrentCount,
    int Capacity,
    WatermarkLevel Level);
