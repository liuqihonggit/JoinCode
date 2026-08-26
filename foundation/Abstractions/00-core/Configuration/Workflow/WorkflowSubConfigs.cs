namespace JoinCode.Abstractions.Configuration;

public class CodeExecutionConfig {
    public int ExecutionTimeoutSeconds { get; set; } = WorkflowConstants.Timeouts.CodeExecutionTimeoutSeconds;
    public int MaxMemoryMB { get; set; } = WorkflowConstants.CodeExecution.MaxMemoryMB;
    public bool AllowNetworkAccess { get; set; } = false;
    public int MaxProcesses { get; set; } = WorkflowConstants.CodeExecution.MaxProcesses;
    public int MaxOpenFiles { get; set; } = WorkflowConstants.CodeExecution.MaxOpenFiles;
    public bool ReadOnlyFilesystem { get; set; } = true;
    public string AllowedDirectories { get; set; } = "/tmp";
}

public class WorktreeConfig {
    /// <summary>
    /// Worktree 目录名（默认 .jcc/worktrees）
    /// </summary>
    public string WorktreesDirectory { get; set; } = WorkflowConstants.Worktree.DefaultWorktreesDirectory;

    /// <summary>
    /// 稀疏检出路径列表（可选）
    /// </summary>
    public List<string> SparsePaths { get; set; } = [];

    /// <summary>
    /// 要符号链接的目录列表
    /// </summary>
    public List<string> SymlinkDirectories { get; set; } = [];

    /// <summary>
    /// 要复制的配置文件列表
    /// </summary>
    public List<string> ConfigFilesToCopy { get; set; } = new() { WorkflowConstants.Paths.LocalSettingsRelativePath };

    /// <summary>
    /// 是否检查未提交更改（默认 true）
    /// </summary>
    public bool CheckUncommittedChanges { get; set; } = true;

    /// <summary>
    /// 是否检查未推送提交（默认 true）
    /// </summary>
    public bool CheckUnpushedCommits { get; set; } = true;

    /// <summary>
    /// 过期时间（天，默认 30）
    /// </summary>
    public int StaleTimeoutDays { get; set; } = WorkflowConstants.Worktree.StaleTimeoutDays;
}

public class IdleDetectionConfig {
    /// <summary>
    /// 是否启用空闲工具检测（默认 true）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 连续多少轮未使用工具后触发提醒（默认 3）
    /// </summary>
    public int MaxIdleRounds { get; set; } = 3;

    /// <summary>
    /// 自定义提醒内容模板（{0} 为连续空闲轮数）
    /// </summary>
    public string? CustomReminderContent { get; set; }
}

/// <summary>
/// 子智能体输出防护配置 — L0-L3 炸窗防护
/// </summary>
public class SubAgentConfig
{
    /// <summary>
    /// L2 自摘要配置
    /// </summary>
    public SubAgentSummaryConfig Summary { get; set; } = new();

    /// <summary>
    /// L3 落盘存档配置
    /// </summary>
    public SubAgentArchiveConfig Archive { get; set; } = new();

    /// <summary>
    /// 算剩余预算 R 时的 reserve token（学 openCode COMPACTION_BUFFER）
    /// </summary>
    public int ReserveTokens { get; set; } = 20_000;

    /// <summary>
    /// 固定输出 token 预算 — IChatContextManager 不可用时的回退值
    /// </summary>
    public int FallbackOutputTokenBudget { get; set; } = 50_000;
}

/// <summary>
/// L2 自摘要配置
/// </summary>
public class SubAgentSummaryConfig
{
    /// <summary>
    /// 是否启用 L2 自摘要（默认 true）。关则跳过 L2，中等超限直接落盘
    /// </summary>
    public bool Auto { get; set; } = true;

    /// <summary>
    /// LLM 调用失败重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 1;
}

/// <summary>
/// L3 落盘存档配置
/// </summary>
public class SubAgentArchiveConfig
{
    /// <summary>
    /// 落盘目录（相对路径，基于当前工作目录）
    /// </summary>
    public string Dir { get; set; } = Path.Combine(".xxx", "subagent");

    /// <summary>
    /// 落盘文件保留天数（学 openCode 7天）
    /// </summary>
    public int RetentionDays { get; set; } = 7;
}
