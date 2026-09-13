namespace Infrastructure.Utils.Resilience;

/// <summary>
/// 韧性遥测报告 — 覆盖所有通讯点的韧性状态
/// </summary>
public sealed class ResilienceTelemetryReport
{
    public required IReadOnlyDictionary<string, HttpResilienceStatus> HttpEndpoints { get; init; }
    public required IReadOnlyDictionary<string, SubprocessResilienceStatus> Subprocesses { get; init; }

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
    public required string Name { get; init; }
    public required CircuitBreakerPhase CircuitBreakerState { get; init; }
    public required int ConsecutiveFailures { get; init; }
    public required int TotalFailures { get; init; }
    public required int TotalSuccesses { get; init; }
    public required DateTimeOffset? LastFailureTime { get; init; }
    public required DateTimeOffset? OpenedAt { get; init; }
}

/// <summary>
/// 子进程韧性状态
/// </summary>
public sealed class SubprocessResilienceStatus
{
    public required string Name { get; init; }
    public required bool IsHealthy { get; init; }
    public required CircuitBreakerPhase CircuitBreakerState { get; init; }
    public required int RestartCount { get; init; }
    public required int MaxRestarts { get; init; }
    public required int ConsecutiveFailures { get; init; }
    public required bool ProcessHasExited { get; init; }
}
