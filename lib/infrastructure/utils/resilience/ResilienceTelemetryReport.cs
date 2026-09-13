namespace Infrastructure.Utils.Resilience;

/// <summary>
/// 韧性遥测报告 — 覆盖所有通讯点的韧性状态
/// </summary>
public sealed class ResilienceTelemetryReport
{
    /// <summary>HTTP 端点韧性状态字典（按端点名称索引）</summary>
    public required IReadOnlyDictionary<string, HttpResilienceStatus> HttpEndpoints { get; init; }
    /// <summary>子进程韧性状态字典（按进程名称索引）</summary>
    public required IReadOnlyDictionary<string, SubprocessResilienceStatus> Subprocesses { get; init; }

    /// <summary>空报告 — 无任何韧性端点</summary>
    public static ResilienceTelemetryReport Empty => new()
    {
        HttpEndpoints = FrozenDictionary<string, HttpResilienceStatus>.Empty,
        Subprocesses = FrozenDictionary<string, SubprocessResilienceStatus>.Empty,
    };
}

/// <summary>
/// HTTP 通讯点韧性状态
/// </summary>
public sealed class HttpResilienceStatus
{
    /// <summary>端点名称</summary>
    public required string Name { get; init; }
    /// <summary>熔断器状态</summary>
    public required CircuitBreakerPhase CircuitBreakerState { get; init; }
    /// <summary>连续失败次数</summary>
    public required int ConsecutiveFailures { get; init; }
    /// <summary>总失败次数</summary>
    public required int TotalFailures { get; init; }
    /// <summary>总成功次数</summary>
    public required int TotalSuccesses { get; init; }
    /// <summary>最近一次失败时间（无失败记录则返回 null）</summary>
    public required DateTimeOffset? LastFailureTime { get; init; }
    /// <summary>熔断器开启时间（未开启过则返回 null）</summary>
    public required DateTimeOffset? OpenedAt { get; init; }
}

/// <summary>
/// 子进程韧性状态
/// </summary>
public sealed class SubprocessResilienceStatus
{
    /// <summary>进程名称</summary>
    public required string Name { get; init; }
    /// <summary>是否健康</summary>
    public required bool IsHealthy { get; init; }
    /// <summary>熔断器状态</summary>
    public required CircuitBreakerPhase CircuitBreakerState { get; init; }
    /// <summary>已重启次数</summary>
    public required int RestartCount { get; init; }
    /// <summary>最大允许重启次数</summary>
    public required int MaxRestarts { get; init; }
    /// <summary>连续失败次数</summary>
    public required int ConsecutiveFailures { get; init; }
    /// <summary>进程是否已退出</summary>
    public required bool ProcessHasExited { get; init; }
}
