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
    public List<string>? SparsePaths { get; set; }

    /// <summary>
    /// 要符号链接的目录列表
    /// </summary>
    public List<string>? SymlinkDirectories { get; set; }

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
