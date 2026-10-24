namespace McpToolDispatch;

/// <summary>工具巡检引擎接口 — 遍历所有工具走完整管道调用，助记符验收，回写健康监控。</summary>
public interface IToolPatrolEngine {
    /// <summary>执行一轮全工具巡检，返回助记符验收报告。</summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>巡检报告（含每个工具助记符结果）</returns>
    Task<ToolPatrolReport> PatrolAsync(CancellationToken ct = default);
}

/// <summary>工具巡检定时服务接口 — 后台定时巡检 + 手动触发一次巡检。</summary>
public interface IToolPatrolService {
    /// <summary>启动后台定时巡检循环。</summary>
    /// <param name="ct">取消令牌</param>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>手动触发一次巡检 — 供测试 + 即时巡检。</summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>巡检报告</returns>
    Task<ToolPatrolReport> PatrolOnceAsync(CancellationToken ct = default);

    /// <summary>停止后台巡检循环。</summary>
    /// <param name="ct">取消令牌</param>
    Task StopAsync(CancellationToken ct = default);
}

/// <summary>工具巡检状态 — 助记符验收每个工具的调用结果。</summary>
public enum ToolPatrolStatus {
    /// <summary>[OK] 成功 — 工具返回非错误结果。</summary>
    [EnumValue("OK")] Ok,
    /// <summary>[ERR] 错误 — 工具返回错误结果（非崩溃，业务级错误）。</summary>
    [EnumValue("ERR")] Error,
    /// <summary>[CRASH] 崩溃 — 工具抛未处理异常。</summary>
    [EnumValue("CRASH")] Crash,
    /// <summary>[LOCK] 死锁 — ActorAskDeadlockException，Actor Ask 超时。</summary>
    [EnumValue("LOCK")] Deadlock,
    /// <summary>[TMO] 超时 — 30s 内未返回。</summary>
    [EnumValue("TMO")] Timeout,
    /// <summary>[SKIP] 跳过 — 有真实副作用（gh/git/desktop/network），巡检不真实调用。</summary>
    [EnumValue("SKIP")] Skipped,
}

/// <summary>单个工具巡检结果 — 助记符 + 工具名 + 错误消息 + 耗时。</summary>
public sealed record ToolPatrolResult(
    string ToolName,
    ToolPatrolStatus Status,
    string? ErrorMessage,
    TimeSpan Elapsed) {
    /// <summary>助记符前缀（[OK]/[ERR]/[CRASH]/[LOCK]/[TMO]/[SKIP]）— 验收报告每行前缀，方便快速扫一眼。</summary>
    public string Mnemonic => Status switch {
        ToolPatrolStatus.Ok => "[OK]",
        ToolPatrolStatus.Error => "[ERR]",
        ToolPatrolStatus.Crash => "[CRASH]",
        ToolPatrolStatus.Deadlock => "[LOCK]",
        ToolPatrolStatus.Timeout => "[TMO]",
        ToolPatrolStatus.Skipped => "[SKIP]",
        _ => $"[{Status}]",
    };
}

/// <summary>工具巡检报告 — 全部工具巡检结果汇总，含助记符统计。</summary>
public sealed record ToolPatrolReport(
    DateTimeOffset Timestamp,
    int TotalTools,
    IReadOnlyList<ToolPatrolResult> Results) {
    /// <summary>[OK] 成功数。</summary>
    public int OkCount => Results.Count(r => r.Status == ToolPatrolStatus.Ok);
    /// <summary>[ERR] 错误数。</summary>
    public int ErrorCount => Results.Count(r => r.Status == ToolPatrolStatus.Error);
    /// <summary>[CRASH] 崩溃数。</summary>
    public int CrashCount => Results.Count(r => r.Status == ToolPatrolStatus.Crash);
    /// <summary>[LOCK] 死锁数。</summary>
    public int DeadlockCount => Results.Count(r => r.Status == ToolPatrolStatus.Deadlock);
    /// <summary>[TMO] 超时数。</summary>
    public int TimeoutCount => Results.Count(r => r.Status == ToolPatrolStatus.Timeout);
    /// <summary>[SKIP] 跳过数。</summary>
    public int SkippedCount => Results.Count(r => r.Status == ToolPatrolStatus.Skipped);

    /// <summary>是否健康通过 — 崩溃/死锁/超时全为 0（错误和跳过允许非零）。</summary>
    public bool IsHealthy => CrashCount == 0 && DeadlockCount == 0 && TimeoutCount == 0;
}
