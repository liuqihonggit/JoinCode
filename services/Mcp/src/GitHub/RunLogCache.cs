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

/// <summary>
/// Run 日志摘要缓存（Level 1，轻量）— 只存步骤名和 section 计数，不存日志行
/// <para>ADR 0067 两级缓存：摘要(轻量)长期保留，内容(大量行)按 section 独立缓存可被驱逐</para>
/// <para>内存压力时 Level 2 内容被优先驱逐,Level 1 摘要保留,AI 仍可看步骤列表和 section 摘要</para>
/// <para>属性用 set(非 init)以支持 JSON 反序列化跨进程持久化</para>
/// </summary>
internal sealed class RunLogSummary
{
    /// <summary>Run ID</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>Job ID(null=无指定 job)</summary>
    public string? JobId { get; set; }

    /// <summary>步骤名 → 该步骤总行数</summary>
    public Dictionary<string, int> StepLineCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>步骤名 → (section类型 → 行数) — section 摘要计数</summary>
    public Dictionary<string, Dictionary<string, int>> SectionCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Run 的 updatedAt(GitHub API) — 用于检测 rerun 后日志是否更新,避免脏数据</summary>
    public string? UpdatedAt { get; set; }

    /// <summary>缓存时间</summary>
    public DateTimeOffset CachedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// GitHub 缓存文件写入 Actor — 单消费者 Channel 串行写文件,不阻塞调用方
/// <para>用 ActorBase 管道(ADR: Actor 模式),TrySendFile 同步入队不阻塞,后台 Consumer 异步写磁盘</para>
/// <para>缓存路径: {projectDir}/.jcc/gh_cache/ — 项目级缓存,跨进程共享</para>
/// <para>通过 IFileSystem 抽象写文件(JCC9001 合规),读取时也走 IFileSystem</para>
/// </summary>
internal sealed class GitHubCacheWriteActor : ActorBase<GitHubCacheWriteActor.ICommand>
{
    internal interface ICommand;

    /// <summary>写入文件命令 — Actor Consumer 串行处理,避免并发写冲突</summary>
    internal sealed record WriteFileCommand(string FilePath, string Content) : ICommand;

    private readonly IFileSystem _fs;
    private readonly ILogger<GitHubCacheWriteActor>? _logger;

    public GitHubCacheWriteActor(IFileSystem fs, ILogger<GitHubCacheWriteActor>? logger = null)
        : base(boundedCapacity: 64, BoundedChannelFullMode.DropOldest)
    {
        _fs = fs;
        _logger = logger;
    }

    /// <summary>
    /// 异步投递写文件命令 — 同步入队不阻塞,Actor Consumer 后台串行写入
    /// </summary>
    internal bool TrySendFile(string filePath, string content)
        => TrySend(new WriteFileCommand(filePath, content));

    protected override async ValueTask HandleAsync(ICommand command, CancellationToken ct)
    {
        if (command is WriteFileCommand w)
        {
            var dir = Path.GetDirectoryName(w.FilePath);
            if (dir is not null && !_fs.DirectoryExists(dir))
                _fs.CreateDirectory(dir);
            await _fs.WriteAllTextAsync(w.FilePath, w.Content, ct).ConfigureAwait(false);
            _logger?.LogDebug("缓存文件已写入: {Path}", w.FilePath);
        }
    }

    protected override void OnConsumerError(Exception ex)
        => _logger?.LogWarning(ex, "缓存文件写入失败");
}

/// <summary>
/// RunLogSummary 的 JSON 序列化上下文 — AOT 模式需要源码生成器
/// </summary>
[JsonSerializable(typeof(RunLogSummary))]
internal sealed partial class RunLogSummaryJsonContext : JsonSerializerContext;
