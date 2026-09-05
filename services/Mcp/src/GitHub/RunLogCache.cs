namespace McpToolDispatch;

/// <summary>
/// 按 GitHub Actions 步骤分组的日志缓存 — 支持按需展开(复刻 ToolSearch map[] 逐层 drill down)
/// <para>流式拉取后按 \t 解析步骤名分组,后续展开从缓存读取,避免重复下载</para>
/// <para>ADR 0067: StepSections 按 ##[error]/##[warning] 等标记分类,支持 Section 级展开</para>
/// </summary>
internal sealed class RunLogCache
{
    /// <summary>Run ID</summary>
    public required string RunId { get; init; }

    /// <summary>Job ID(null=无指定 job)</summary>
    public string? JobId { get; init; }

    /// <summary>步骤名 → 该步骤的日志行列表(日志行格式: JobName\tStepName\tTimestamp\tLogLine)</summary>
    public Dictionary<string, List<string>> Steps { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>步骤名 → (section类型 → 该类型的日志行列表) — 按标记分类,支持逐级展开(ADR 0067)</summary>
    public Dictionary<string, Dictionary<string, List<string>>> StepSections { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>缓存时间</summary>
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;

    // === Section 类型常量 — 按 GitHub Actions 日志标记分类 ===

    internal const string SectionError = "error";
    internal const string SectionWarning = "warning";
    internal const string SectionCommand = "command";
    internal const string SectionGroup = "group";
    internal const string SectionNormal = "normal";

    /// <summary>
    /// 解析日志行的 section 类型 — 按 GitHub Actions 标记分类
    /// <para>##[error] → error, ##[warning] → warning, ##[command] → command, ##[group] → group, 无标记 → normal</para>
    /// </summary>
    internal static string ParseSectionType(string line)
    {
        if (line.Contains("##[error]", StringComparison.OrdinalIgnoreCase)) return SectionError;
        if (line.Contains("##[warning]", StringComparison.OrdinalIgnoreCase)) return SectionWarning;
        if (line.Contains("##[command]", StringComparison.OrdinalIgnoreCase)) return SectionCommand;
        if (line.Contains("##[group]", StringComparison.OrdinalIgnoreCase)) return SectionGroup;
        return SectionNormal;
    }
}
