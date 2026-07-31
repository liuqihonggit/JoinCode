namespace Infrastructure.Utils.Resilience;

using Infrastructure.Http;

/// <summary>
/// 韧性遥测收集器 — 从 ResilientHttpClientProvider 和 ResilientSubprocess 收集状态
/// </summary>
public sealed class ResilienceTelemetryCollector
{
    private readonly IResilientHttpClientProvider? _httpClientProvider;
    private readonly ILogger? _logger;

    public ResilienceTelemetryCollector(
        IResilientHttpClientProvider? httpClientProvider = null,
        ILogger? logger = null)
    {
        _httpClientProvider = httpClientProvider;
        _logger = logger;
    }

    public ResilienceTelemetryReport Collect()
    {
        var httpEndpoints = new Dictionary<string, HttpResilienceStatus>();

        if (_httpClientProvider is ResilientHttpClientProvider resilientProvider)
        {
            var executor = resilientProvider.Executor;
            var cb = executor.CircuitBreaker;
            if (cb is not null)
            {
                httpEndpoints[cb.Name] = new HttpResilienceStatus
                {
                    Name = cb.Name,
                    CircuitBreakerState = cb.State,
                    ConsecutiveFailures = cb.ConsecutiveFailures,
                    TotalFailures = cb.TotalFailures,
                    TotalSuccesses = cb.TotalSuccesses,
                    LastFailureTime = cb.LastFailureTime,
                    OpenedAt = cb.OpenedAt,
                };
            }
        }

        return new ResilienceTelemetryReport
        {
            HttpEndpoints = httpEndpoints,
            Subprocesses = FrozenDictionary<string, SubprocessResilienceStatus>.Empty,
        };
    }

    /// <summary>
    /// 格式化报告为可读文本（用于 jcc doctor --resilience）
    /// </summary>
    public static string Format(ResilienceTelemetryReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 韧性状态报告 ===");
        sb.AppendLine();

        if (report.HttpEndpoints.Count == 0 && report.Subprocesses.Count == 0)
        {
            sb.AppendLine("（无韧性端点注册）");
            return sb.ToString();
        }

        if (report.HttpEndpoints.Count > 0)
        {
            sb.AppendLine("--- HTTP 端点 ---");
            foreach (var kvp in report.HttpEndpoints)
            {
                var s = kvp.Value;
                var stateIcon = s.CircuitBreakerState switch
                {
                    CircuitBreakerPhase.Closed => "🟢",
                    CircuitBreakerPhase.HalfOpen => "🟡",
                    CircuitBreakerPhase.Open => "🔴",
                    _ => "❓"
                };
                sb.AppendLine($"  {stateIcon} {s.Name}: 熔断={s.CircuitBreakerState}, 连续失败={s.ConsecutiveFailures}, 总失败={s.TotalFailures}, 总成功={s.TotalSuccesses}");
            }
            sb.AppendLine();
        }

        if (report.Subprocesses.Count > 0)
        {
            sb.AppendLine("--- 子进程 ---");
            foreach (var kvp in report.Subprocesses)
            {
                var s = kvp.Value;
                var healthIcon = s.IsHealthy ? "🟢" : "🔴";
                sb.AppendLine($"  {healthIcon} {s.Name}: 健康={s.IsHealthy}, 熔断={s.CircuitBreakerState}, 重启={s.RestartCount}/{s.MaxRestarts}, 已退出={s.ProcessHasExited}");
            }
        }

        return sb.ToString();
    }
}
