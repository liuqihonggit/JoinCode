namespace McpToolDispatch;

/// <summary>
/// CI job 严重级别状态机 — [Flags] 二次方值支持位组合,按 错误 > 超时 > 取消 > 进行中 > 通过 优先级排序。
/// <para>组合用法: <c>severity &amp; CiJobSeverity.Anomaly != 0</c> 判断是否为异常状态(failure+timed_out+cancelled)</para>
/// <para>显示策略: 异常完整展开,通过默认折叠 — AI 首屏定位问题降 token</para>
/// </summary>
[Flags]
public enum CiJobSeverity {
    /// <summary>无状态</summary>
    None      = 0,
    /// <summary>失败 — 最严重,排序最前</summary>
    Failure   = 1,       // 2^0
    /// <summary>超时 — job 被取消因超时</summary>
    TimedOut  = 2,       // 2^1
    /// <summary>取消 — 非超时原因</summary>
    Cancelled = 4,       // 2^2
    /// <summary>进行中/排队</summary>
    Pending   = 8,       // 2^3
    /// <summary>通过 — 默认折叠</summary>
    Success   = 16,      // 2^4
    /// <summary>跳过 — 依赖链跳过,非失败</summary>
    Skipped   = 32,      // 2^5
    /// <summary>异常组合 — failure + timed_out + cancelled,需完整展开显示</summary>
    Anomaly   = Failure | TimedOut | Cancelled,
}

/// <summary>CiJobSeverity 扩展方法 — 从 GitHub API conclusion 映射 + 排序 + 显示标记</summary>
public static class CiJobSeverityExtensions {
    /// <summary>从 GitHub API job conclusion + status 映射到 CiJobSeverity</summary>
    /// <param name="conclusion">job conclusion(failure/success/cancelled/timed_out/skipped/null)</param>
    /// <param name="status">job status(queued/in_progress/completed 等)</param>
    public static CiJobSeverity FromConclusion(string? conclusion, string? status = null)
        => (conclusion, status) switch {
            ("failure", _)    => CiJobSeverity.Failure,
            ("timed_out", _)  => CiJobSeverity.TimedOut,
            ("cancelled", _)  => CiJobSeverity.Cancelled,
            ("skipped", _)    => CiJobSeverity.Skipped,
            ("success", _)    => CiJobSeverity.Success,
            (_, "in_progress") => CiJobSeverity.Pending,
            (_, "queued")     => CiJobSeverity.Pending,
            (null or "", _)   => CiJobSeverity.Pending,
            _                 => CiJobSeverity.None,
        };

    /// <summary>判断是否为异常状态(failure + timed_out + cancelled) — 需完整展开显示</summary>
    public static bool IsAnomaly(this CiJobSeverity severity)
        => (severity & CiJobSeverity.Anomaly) != CiJobSeverity.None;

    /// <summary>排序键 — 异常在前(0-2),进行中(3),通过(4),跳过(5),未知(6)</summary>
    public static int SortKey(this CiJobSeverity severity)
        => severity switch {
            CiJobSeverity.Failure   => 0,
            CiJobSeverity.TimedOut  => 1,
            CiJobSeverity.Cancelled => 2,
            CiJobSeverity.Pending   => 3,
            CiJobSeverity.Success   => 4,
            CiJobSeverity.Skipped   => 5,
            _                       => 6,
        };

    /// <summary>显示标记符号 — ❌(失败) ⏱(超时) ⊘(取消) ⏳(进行中) ✅(通过) ⊟(跳过)</summary>
    public static string Marker(this CiJobSeverity severity)
        => severity switch {
            CiJobSeverity.Failure   => "❌",
            CiJobSeverity.TimedOut  => "⏱",
            CiJobSeverity.Cancelled => "⊘",
            CiJobSeverity.Pending   => "⏳",
            CiJobSeverity.Success   => "✅",
            CiJobSeverity.Skipped   => "⊟",
            _                       => "  ",
        };
}
