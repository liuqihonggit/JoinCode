namespace Core.Utils;

/// <summary>
/// Actor Ask 模式死锁异常 — Ask 超时后抛出,带诊断信息指导修复。
/// </summary>
/// <remarks>
/// <para>触发条件:AskAwait 超时(默认10s) — Consumer 未在超时内处理命令并设置 Tcs。</para>
/// <para>常见根因:线程池饥饿 — 所有线程被阻塞等待,Consumer 任务无法被调度。</para>
/// <para>修复指导:Dispose 路径改用 Tell(TrySend);查询路径检查 Consumer 是否阻塞或线程池是否不足。</para>
/// </remarks>
public sealed class ActorAskDeadlockException : TimeoutException {
    /// <summary>Actor 类型名</summary>
    public string ActorName { get; }

    /// <summary>超时毫秒数</summary>
    public int TimeoutMs { get; }

    /// <summary>
    /// 构造 Ask 死锁异常
    /// </summary>
    /// <param name="actorName">Actor 类型名</param>
    /// <param name="timeoutMs">超时毫秒数</param>
    public ActorAskDeadlockException(string actorName, int timeoutMs)
        : base($"Actor {actorName} Ask 超时({timeoutMs}ms) — 可能线程池饥饿导致 Consumer 无法调度。" +
               "Dispose 路径改用 Tell(TrySend);查询路径检查 Consumer 是否阻塞或线程池是否不足。") {
        ActorName = actorName;
        TimeoutMs = timeoutMs;
    }
}

/// <summary>
/// Actor 循环 Ask 异常 — 等待图检测到环时抛出,预防循环 Ask 死锁(类型2)。
/// </summary>
/// <remarks>
/// <para>触发条件:AskAwait 检测到等待图环 — Actor A 等 B 回复,同时 B 等 A 回复。</para>
/// <para>检测机制:静态等待图(wait-for graph),通过线程ID自动识别调用方Actor,记入等待边,检测到环即抛异常。</para>
/// <para>修复指导:打破循环 — 其中一方改用 Tell(不等回复),或重构调用链消除循环依赖。</para>
/// </remarks>
public sealed class ActorCyclicAskException : InvalidOperationException {
    /// <summary>调用方 Actor ID</summary>
    public string CallerActorId { get; }

    /// <summary>目标 Actor ID</summary>
    public string TargetActorId { get; }

    /// <summary>
    /// 构造循环 Ask 异常
    /// </summary>
    /// <param name="callerActorId">调用方 Actor ID</param>
    /// <param name="targetActorId">目标 Actor ID</param>
    public ActorCyclicAskException(string callerActorId, string targetActorId)
        : base($"循环 Ask 检测: Actor {callerActorId} 等 {targetActorId} 回复,同时 {targetActorId} 等 {callerActorId} 回复 → 等待图环 → 死锁。" +
               "修复:其中一方改用 Tell(TrySend,不等回复),或重构调用链消除循环依赖。") {
        CallerActorId = callerActorId;
        TargetActorId = targetActorId;
    }
}
