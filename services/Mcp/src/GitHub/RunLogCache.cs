namespace McpToolDispatch;

/// <summary>
/// 按 GitHub Actions 步骤分组的日志缓存 — 支持按需展开(复刻 ToolSearch map[] 逐层 drill down)
/// <para>流式拉取后按 \t 解析步骤名分组,后续展开从缓存读取,避免重复下载</para>
/// </summary>
internal sealed class RunLogCache
{
    /// <summary>Run ID</summary>
    public required string RunId { get; init; }

    /// <summary>Job ID(null=无指定 job)</summary>
    public string? JobId { get; init; }

    /// <summary>步骤名 → 该步骤的日志行列表(日志行格式: JobName\tStepName\tTimestamp\tLogLine)</summary>
    public Dictionary<string, List<string>> Steps { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>缓存时间</summary>
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}
