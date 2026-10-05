namespace JoinCode.Abstractions.Configuration;

public sealed record CodeExecutionConfig {
    /// <summary>获取或设置执行超时时间(秒)。</summary>
    public int ExecutionTimeoutSeconds { get; init; } = WorkflowConstants.Timeouts.CodeExecutionTimeoutSeconds;
    /// <summary>获取或设置最大内存(MB)。</summary>
    public int MaxMemoryMB { get; init; } = WorkflowConstants.CodeExecution.MaxMemoryMB;
    /// <summary>获取或设置是否允许网络访问。</summary>
    public bool AllowNetworkAccess { get; init; } = false;
    /// <summary>获取或设置最大进程数。</summary>
    public int MaxProcesses { get; init; } = WorkflowConstants.CodeExecution.MaxProcesses;
    /// <summary>获取或设置最大打开文件数。</summary>
    public int MaxOpenFiles { get; init; } = WorkflowConstants.CodeExecution.MaxOpenFiles;
    /// <summary>获取或设置是否只读文件系统。</summary>
    public bool ReadOnlyFilesystem { get; init; } = true;
    /// <summary>获取或设置允许访问的目录。</summary>
    public string AllowedDirectories { get; init; } = "/tmp";
}

public sealed record WorktreeConfig {
    /// <summary>
    /// Worktree 目录名（默认 .jcc/worktrees）
    /// </summary>
    public string WorktreesDirectory { get; init; } = WorkflowConstants.Worktree.DefaultWorktreesDirectory;

    /// <summary>
    /// 稀疏检出路径列表（可选）
    /// </summary>
    public List<string> SparsePaths { get; init; } = [];

    /// <summary>
    /// 要符号链接的目录列表
    /// </summary>
    public List<string> SymlinkDirectories { get; init; } = [];

    /// <summary>
    /// 要复制的配置文件列表
    /// </summary>
    public List<string> ConfigFilesToCopy { get; init; } = new() { AppDataConstants.Paths.LocalSettingsRelativePath };

    /// <summary>
    /// 是否检查未提交更改（默认 true）
    /// </summary>
    public bool CheckUncommittedChanges { get; init; } = true;

    /// <summary>
    /// 是否检查未推送提交（默认 true）
    /// </summary>
    public bool CheckUnpushedCommits { get; init; } = true;

    /// <summary>
    /// 过期时间（天，默认 30）
    /// </summary>
    public int StaleTimeoutDays { get; init; } = WorkflowConstants.Worktree.StaleTimeoutDays;
}

public sealed record IdleDetectionConfig {
    /// <summary>
    /// 是否启用空闲工具检测（默认 true）
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 连续多少轮未使用工具后触发提醒（默认 3）
    /// </summary>
    public int MaxIdleRounds { get; init; } = 3;

    /// <summary>
    /// 自定义提醒内容模板（{0} 为连续空闲轮数）
    /// </summary>
    public string? CustomReminderContent { get; init; }
}

/// <summary>
/// 子智能体输出防护配置 — L0-L3 炸窗防护
/// </summary>
public sealed record SubAgentConfig {
    /// <summary>
    /// L2 自摘要配置
    /// </summary>
    public SubAgentSummaryConfig Summary { get; init; } = new();

    /// <summary>
    /// L3 落盘存档配置
    /// </summary>
    public SubAgentArchiveConfig Archive { get; init; } = new();

    /// <summary>
    /// 算剩余预算 R 时的 reserve token（学 openCode COMPACTION_BUFFER）
    /// </summary>
    public int ReserveTokens { get; init; } = 20_000;

    /// <summary>
    /// 固定输出 token 预算 — IChatContextManager 不可用时的回退值
    /// </summary>
    public int FallbackOutputTokenBudget { get; init; } = 50_000;
}

/// <summary>
/// L2 自摘要配置
/// </summary>
public sealed record SubAgentSummaryConfig {
    /// <summary>
    /// 是否启用 L2 自摘要（默认 true）。关则跳过 L2，中等超限直接落盘
    /// </summary>
    public bool Auto { get; init; } = true;

    /// <summary>
    /// LLM 调用失败重试次数
    /// </summary>
    public int MaxRetries { get; init; } = 1;
}

/// <summary>
/// L3 落盘存档配置
/// </summary>
public sealed record SubAgentArchiveConfig {
    /// <summary>
    /// 落盘目录（相对路径，基于当前工作目录）
    /// </summary>
    public string Dir { get; init; } = Path.Combine(".xxx", "subagent");

    /// <summary>
    /// 落盘文件保留天数（学 openCode 7天）
    /// </summary>
    public int RetentionDays { get; init; } = 7;
}
