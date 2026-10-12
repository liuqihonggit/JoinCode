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
    /// <summary>警告 — stale/action_required,非失败但需注意</summary>
    Warning   = 64,      // 2^6
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
            ("failure", _)          => CiJobSeverity.Failure,
            ("timed_out", _)        => CiJobSeverity.TimedOut,
            ("cancelled", _)        => CiJobSeverity.Cancelled,
            ("skipped", _)          => CiJobSeverity.Skipped,
            ("neutral", _)          => CiJobSeverity.Skipped,
            ("stale", _)            => CiJobSeverity.Warning,
            ("action_required", _)  => CiJobSeverity.Warning,
            ("success", _)          => CiJobSeverity.Success,
            (_, "in_progress")     => CiJobSeverity.Pending,
            (_, "queued")          => CiJobSeverity.Pending,
            (null or "", _)        => CiJobSeverity.Pending,
            _                      => CiJobSeverity.None,
        };

    /// <summary>判断是否为异常状态(failure + timed_out + cancelled) — 需完整展开显示</summary>
    public static bool IsAnomaly(this CiJobSeverity severity)
        => (severity & CiJobSeverity.Anomaly) != CiJobSeverity.None;

    /// <summary>排序键 — 异常在前(0-2),警告(3),进行中(4),通过(5),跳过(6),未知(7)</summary>
    public static int SortKey(this CiJobSeverity severity)
        => severity switch {
            CiJobSeverity.Failure   => 0,
            CiJobSeverity.TimedOut  => 1,
            CiJobSeverity.Cancelled => 2,
            CiJobSeverity.Warning   => 3,
            CiJobSeverity.Pending   => 4,
            CiJobSeverity.Success   => 5,
            CiJobSeverity.Skipped   => 6,
            _                       => 7,
        };

    /// <summary>显示标记符号 — ❌(失败) ⏱(超时) ⊘(取消) ⚠(警告) ⏳(进行中) ✅(通过) ⊟(跳过)</summary>
    public static string Marker(this CiJobSeverity severity)
        => severity switch {
            CiJobSeverity.Failure   => "❌",
            CiJobSeverity.TimedOut  => "⏱",
            CiJobSeverity.Cancelled => "⊘",
            CiJobSeverity.Warning   => "⚠",
            CiJobSeverity.Pending   => "⏳",
            CiJobSeverity.Success   => "✅",
            CiJobSeverity.Skipped   => "⊟",
            _                       => "  ",
        };

    /// <summary>短显示名 — fail/warning/pending/skipping/pass,对齐 gh pr checks 原始输出格式</summary>
    internal static string ToDisplayShort(this CiJobSeverity severity)
        => severity switch {
            CiJobSeverity.Failure or CiJobSeverity.TimedOut or CiJobSeverity.Cancelled => "fail",
            CiJobSeverity.Warning   => "warning",
            CiJobSeverity.Pending   => "pending",
            CiJobSeverity.Skipped   => "skipping",
            CiJobSeverity.Success   => "pass",
            _                       => "pending",
        };

    /// <summary>GraphQL StatusState 映射 — SUCCESS/FAILURE/NEUTRAL/PENDING,对齐 GraphQL statusCheckRollup</summary>
    internal static string ToGraphQLState(this CiJobSeverity severity)
        => severity switch {
            CiJobSeverity.Success => "SUCCESS",
            CiJobSeverity.Failure or CiJobSeverity.TimedOut or CiJobSeverity.Cancelled => "FAILURE",
            CiJobSeverity.Pending => "PENDING",
            CiJobSeverity.Skipped or CiJobSeverity.Warning => "NEUTRAL",
            _ => "PENDING",
        };
}

/// <summary>
/// CI 日志分析统一工具类 — 唯一数据源,所有日志错误检测/分类/摘要/section 解析逻辑集中于此。
/// <para>消除散落在 GitHubRunLogFilterRunner/GitHubRunLogFilter/RunLogCache 等多文件的重复硬编码</para>
/// <para>消费方统一委托到此: IsErrorIndicator → ClassifyError → BuildErrorSummary,常量引用 CiLogAnalyzer.XxxMarker</para>
/// </summary>
internal static class CiLogAnalyzer {
    // === GitHub Actions 日志标记常量 — 唯一数据源,禁止在其他文件硬编码 ===

    /// <summary>GitHub Actions 错误标记</summary>
    internal const string ErrorMarker   = "##[error]";
    /// <summary>GitHub Actions 警告标记</summary>
    internal const string WarningMarker = "##[warning]";
    /// <summary>GitHub Actions 命令标记</summary>
    internal const string CommandMarker = "##[command]";
    /// <summary>GitHub Actions 分组标记</summary>
    internal const string GroupMarker   = "##[group]";
    /// <summary>进程退出码标记</summary>
    internal const string ExitCodeMarker = "Process completed with exit code";
    /// <summary>.NET native library 加载失败异常</summary>
    internal const string DllNotFoundMarker = "System.DllNotFoundException";
    /// <summary>.NET 类型初始化失败异常（静态构造函数抛异常）</summary>
    internal const string TypeInitMarker = "System.TypeInitializationException";

    // === Section 类型常量 ===

    /// <summary>error section</summary>
    internal const string SectionError   = "error";
    /// <summary>warning section</summary>
    internal const string SectionWarning = "warning";
    /// <summary>command section</summary>
    internal const string SectionCommand = "command";
    /// <summary>group section</summary>
    internal const string SectionGroup   = "group";
    /// <summary>normal section</summary>
    internal const string SectionNormal  = "normal";

    /// <summary>
    /// 错误指示行检测 — GitHub Actions 标记 + 编译/测试/链接错误 + 超时/取消模式,精确定位真正错误行
    /// <para>扩展模式: error CS####(C#编译) / error MSB####(MSBuild) / [FAIL](xUnit) / Failed (测试汇总) / 超时/取消</para>
    /// <para>移除 No test matches — 非错误,大量测试项目无匹配是正常行为,产生噪音淹没真正错误</para>
    /// </summary>
    internal static bool IsErrorIndicator(ReadOnlySpan<char> line)
        => line.Contains(ErrorMarker, StringComparison.OrdinalIgnoreCase)
        || line.Contains(ExitCodeMarker, StringComparison.OrdinalIgnoreCase)
        || line.Contains("error CS", StringComparison.OrdinalIgnoreCase)
        || line.Contains("error MSB", StringComparison.OrdinalIgnoreCase)
        || line.Contains("[FAIL]", StringComparison.OrdinalIgnoreCase)
        || line.Contains("  Failed ", StringComparison.OrdinalIgnoreCase)
        || line.Contains("warning MSB9008", StringComparison.OrdinalIgnoreCase)
        || line.Contains("was canceled", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Cancelling the job", StringComparison.OrdinalIgnoreCase)
        || line.Contains("exceeded the maximum", StringComparison.OrdinalIgnoreCase)
        || line.Contains("timed out", StringComparison.OrdinalIgnoreCase)
        || line.Contains(DllNotFoundMarker, StringComparison.Ordinal)
        || line.Contains(TypeInitMarker, StringComparison.Ordinal);

    /// <summary>错误行分类 — 从错误行内容提取类型,用于摘要统计</summary>
    internal static CiErrorCategory ClassifyError(ReadOnlySpan<char> line) {
        if (line.Contains(DllNotFoundMarker, StringComparison.Ordinal) || line.Contains(TypeInitMarker, StringComparison.Ordinal)) return CiErrorCategory.RuntimeException;
        if (line.Contains("error CS", StringComparison.OrdinalIgnoreCase)) return CiErrorCategory.CsCompile;
        if (line.Contains("error MSB", StringComparison.OrdinalIgnoreCase) || line.Contains("warning MSB", StringComparison.OrdinalIgnoreCase)) return CiErrorCategory.MsBuild;
        if (line.Contains("[FAIL]", StringComparison.OrdinalIgnoreCase) || line.Contains("  Failed ", StringComparison.OrdinalIgnoreCase)) return CiErrorCategory.TestFailure;
        if (line.Contains("was canceled", StringComparison.OrdinalIgnoreCase) || line.Contains("Cancelling the job", StringComparison.OrdinalIgnoreCase)) return CiErrorCategory.Cancelled;
        if (line.Contains("exceeded the maximum", StringComparison.OrdinalIgnoreCase) || line.Contains("timed out", StringComparison.OrdinalIgnoreCase)) return CiErrorCategory.Timeout;
        if (line.Contains(ExitCodeMarker, StringComparison.OrdinalIgnoreCase)) return CiErrorCategory.ProcessExit;
        if (line.Contains(ErrorMarker, StringComparison.OrdinalIgnoreCase)) return CiErrorCategory.ActionsError;
        return CiErrorCategory.Other;
    }

    /// <summary>构建错误摘要行 — 错误数+类型分布,前置到 filter=error 输出开头</summary>
    internal static string BuildErrorSummary(int errorCount, Dictionary<CiErrorCategory, int> categories) {
        var parts = new List<string>(categories.Count);
        foreach (var (cat, cnt) in categories.OrderByDescending(x => x.Value))
            parts.Add($"{cnt}x {cat.ToDisplay()}");
        return $"📊 发现 {errorCount} 个错误: {string.Join(", ", parts)}";
    }

    /// <summary>
    /// 检测日志文本是否只有 "Process completed with exit code N" 但缺少栈帧信息
    /// <para>栈帧标记: "  at " / "Exception" / "StackTrace" / "stack trace" — 有任一即视为有栈帧</para>
    /// </summary>
    internal static bool HasNoStackTrace(string text) {
        if (!text.Contains(ExitCodeMarker, StringComparison.OrdinalIgnoreCase))
            return false;
        return !text.Contains("  at ", StringComparison.Ordinal)
            && !text.Contains("Exception", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("StackTrace", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("stack trace", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解析日志行的 section 类型 — 按 GitHub Actions 标记分类
    /// <para>##[error] → error, ##[warning] → warning, ##[command] → command, ##[group] → group, 无标记 → normal</para>
    /// </summary>
    internal static string ParseSectionType(string line) {
        if (line.Contains(ErrorMarker, StringComparison.OrdinalIgnoreCase)) return SectionError;
        if (line.Contains(WarningMarker, StringComparison.OrdinalIgnoreCase)) return SectionWarning;
        if (line.Contains(CommandMarker, StringComparison.OrdinalIgnoreCase)) return SectionCommand;
        if (line.Contains(GroupMarker, StringComparison.OrdinalIgnoreCase)) return SectionGroup;
        return SectionNormal;
    }
}

/// <summary>CI 错误分类 — 用于错误摘要统计,按类型分布让 AI 一眼看到错误规模</summary>
internal enum CiErrorCategory {
    /// <summary>C# 编译错误 (error CS####)</summary>
    CsCompile,
    /// <summary>MSBuild 错误 (error MSB####)</summary>
    MsBuild,
    /// <summary>测试失败 ([FAIL] / Failed)</summary>
    TestFailure,
    /// <summary>取消 (was canceled / Cancelling the job)</summary>
    Cancelled,
    /// <summary>超时 (exceeded the maximum / timed out)</summary>
    Timeout,
    /// <summary>进程退出 (Process completed with exit code)</summary>
    ProcessExit,
    /// <summary>GitHub Actions 错误 (##[error])</summary>
    ActionsError,
    /// <summary>.NET 运行时异常 (DllNotFoundException / TypeInitializationException)</summary>
    RuntimeException,
    /// <summary>其他错误</summary>
    Other,
}

/// <summary>CiErrorCategory 显示扩展 — 中文显示名</summary>
internal static class CiErrorCategoryExtensions {
    /// <summary>获取错误分类的中文显示名</summary>
    internal static string ToDisplay(this CiErrorCategory category) => category switch {
        CiErrorCategory.CsCompile    => "CS编译",
        CiErrorCategory.MsBuild      => "MSBuild",
        CiErrorCategory.TestFailure  => "测试失败",
        CiErrorCategory.Cancelled    => "取消",
        CiErrorCategory.Timeout      => "超时",
        CiErrorCategory.ProcessExit  => "进程退出",
        CiErrorCategory.ActionsError => "Actions错误",
        CiErrorCategory.RuntimeException => "运行时异常",
        _                            => "其他",
    };
}
