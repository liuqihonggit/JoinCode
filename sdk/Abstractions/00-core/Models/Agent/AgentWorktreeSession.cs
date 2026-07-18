
namespace JoinCode.Abstractions.Models;

/// <summary>
/// 智能体 Worktree 会话信息
/// </summary>
public sealed record AgentWorktreeSession {
    /// <summary>
    /// 智能体 ID
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// 原始工作目录
    /// </summary>
    public required string OriginalCwd { get; init; }

    /// <summary>
    /// Worktree 路径
    /// </summary>
    public required string WorktreePath { get; init; }

    /// <summary>
    /// Git 分支名
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Git 仓库根目录
    /// </summary>
    public required string GitRootPath { get; init; }

    /// <summary>
    /// 原始分支名（创建 worktree 前的分支）
    /// </summary>
    public string? OriginalBranch { get; init; }

    /// <summary>
    /// 基础提交 SHA
    /// </summary>
    public string? BaseCommitSha { get; init; }

    /// <summary>
    /// 会话创建时间
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// 是否已存在（快速恢复）
    /// </summary>
    public bool Existed { get; init; }

    /// <summary>
    /// 是否基于 Hook（而非 Git）
    /// </summary>
    public bool HookBased { get; init; }

    /// <summary>
    /// 使用的稀疏检出路径（如果有）
    /// </summary>
    public IReadOnlyList<string>? SparsePaths { get; init; }

    /// <summary>
    /// 创建时长（毫秒），null 表示是恢复现有 worktree
    /// </summary>
    public long? CreationDurationMs { get; init; }

    /// <summary>
    /// 生成 worktree 分支名
    /// </summary>
    public static string GenerateBranchName(string agentId) {
        var safeId = agentId.Replace("/", "+").Replace("\\", "+");
        return $"worktree-{safeId}";
    }

    /// <summary>
    /// 生成 worktree 路径
    /// </summary>
    public static string GenerateWorktreePath(string gitRoot, string agentId) {
        var safeId = agentId.Replace("/", "+").Replace("\\", "+");
        return WorkflowConstants.Paths.GetProjectWorktreePath(gitRoot, agentId);
    }

    /// <summary>
    /// 解析 PR 引用 — 对齐 TS parsePRReference
    /// 支持 #N 格式和 GitHub PR URL（如 https://github.com/owner/repo/pull/123）
    /// </summary>
    public static int? ParsePRReference(string input) {
        var urlMatch = Regex.Match(input, @"^https?://[^/]+/[^/]+/[^/]+/pull/(\d+)/?(?:[?#].*)?$", RegexOptions.IgnoreCase);
        if (urlMatch.Success && int.TryParse(urlMatch.Groups[1].Value, out var urlPrNum))
        {
            return urlPrNum;
        }

        var hashMatch = Regex.Match(input, @"^#(\d+)$");
        if (hashMatch.Success && int.TryParse(hashMatch.Groups[1].Value, out var hashPrNum))
        {
            return hashPrNum;
        }

        return null;
    }
}

/// <summary>
/// Worktree 配置选项
/// </summary>
public sealed record WorktreeOptions {
    /// <summary>
    /// 基础分支或提交（可选）
    /// </summary>
    public string? BaseBranch { get; init; }

    /// <summary>
    /// PR 编号（可选）— 对齐 TS parsePRReference，支持 #N 和 GitHub PR URL
    /// </summary>
    public int? PrNumber { get; init; }

    /// <summary>
    /// 稀疏检出路径列表（可选）
    /// </summary>
    public IReadOnlyList<string>? SparsePaths { get; init; }

    /// <summary>
    /// 要符号链接的目录列表（避免磁盘膨胀）
    /// </summary>
    public IReadOnlyList<string>? SymlinkDirectories { get; init; }

    /// <summary>
    /// 要复制的配置文件列表
    /// </summary>
    public IReadOnlyList<string>? ConfigFilesToCopy { get; init; } =
    [
        WorkflowConstants.Paths.LocalSettingsRelativePath
    ];

    /// <summary>
    /// 是否检查未提交更改（默认 true）
    /// </summary>
    public bool CheckUncommittedChanges { get; init; } = true;

    /// <summary>
    /// 是否检查未推送提交（默认 true）
    /// </summary>
    public bool CheckUnpushedCommits { get; init; } = true;

    /// <summary>
    /// 过期时间（默认 30 天）
    /// </summary>
    public TimeSpan StaleTimeout { get; init; } = TimeSpan.FromDays(30);

    /// <summary>
    /// 过期时间（小时，便捷属性）
    /// </summary>
    public int StaleTimeoutHours {
        get => (int)StaleTimeout.TotalHours;
        init => StaleTimeout = TimeSpan.FromHours(value);
    }

    /// <summary>
    /// 临时 worktree 名称模式（用于自动清理）
    /// 对齐 TS EPHEMERAL_WORKTREE_PATTERNS：精确正则匹配，避免误删用户命名的 worktree
    /// </summary>
    public IReadOnlyList<string> EphemeralPatterns { get; init; } =
    [
        "^agent-[a-z0-9]{1,8}$",
        "^wf_[0-9a-f]{8}-[0-9a-f]{3}-\\d+$",
        "^wf-\\d+$",
        "^bridge-[A-Za-z0-9_]+(-[A-Za-z0-9_]+)*$",
        "^job-[a-zA-Z0-9._-]{1,55}-[0-9a-f]{8}$"
    ];
}

/// <summary>
/// Worktree 创建结果
/// </summary>
public sealed record WorktreeCreateResult {
    /// <summary>
    /// 是否成功
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 会话信息
    /// </summary>
    public AgentWorktreeSession? Session { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 是否已存在
    /// </summary>
    public bool Existed { get; init; }

    public static WorktreeCreateResult SuccessResult(AgentWorktreeSession session, bool existed = false) {
        return new WorktreeCreateResult {
            Success = true,
            Session = session,
            Existed = existed
        };
    }

    public static WorktreeCreateResult FailureResult(string errorMessage) {
        return new WorktreeCreateResult {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// Worktree 清理结果
/// </summary>
public sealed record WorktreeCleanupResult {
    /// <summary>
    /// 是否成功
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 被阻止的原因（如果有未提交更改等）
    /// </summary>
    public string? BlockReason { get; init; }

    /// <summary>
    /// 是否已强制清理
    /// </summary>
    public bool Forced { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; init; }

    public static WorktreeCleanupResult SuccessResult(bool forced = false) {
        return new WorktreeCleanupResult {
            Success = true,
            Forced = forced
        };
    }

    public static WorktreeCleanupResult BlockedResult(string reason) {
        return new WorktreeCleanupResult {
            Success = false,
            BlockReason = reason
        };
    }

    public static WorktreeCleanupResult FailureResult(string errorMessage) {
        return new WorktreeCleanupResult {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}
