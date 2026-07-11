


namespace McpToolHandlers;

/// <summary>
/// Worktree工具处理器 - 提供Git Worktree隔离管理功能
/// </summary>
[McpToolHandler(ToolCategory.Worktree)]
public class WorktreeToolHandlers
{
    private readonly IAgentWorktreeService _worktreeService;
    private readonly IFileSystem _fs;

    public WorktreeToolHandlers(IAgentWorktreeService worktreeService, IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(worktreeService);
        ArgumentNullException.ThrowIfNull(fs);
        _worktreeService = worktreeService;
        _fs = fs;
    }

    /// <summary>
    /// 创建代理Worktree
    /// </summary>
    [McpTool(WorktreeToolNameConstants.WorktreeCreate, "Create a Git Worktree isolated environment for an agent", "worktree")]
    public async Task<ToolResult> WorktreeCreateAsync(
        [McpToolParameter("Agent ID")] string agent_id,
        [McpToolParameter("Git repository root directory (optional, auto-detected)", Required = false)] string? git_root = null,
        [McpToolParameter("Base branch or commit (optional)", Required = false)] string? base_branch = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_id))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.WorktreeAgentIdCannotBeEmpty)).Build();
        }

        var options = new WorktreeOptions
        {
            BaseBranch = base_branch
        };

        var result = await _worktreeService.CreateAgentWorktreeAsync(agent_id, git_root, options, cancellationToken);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.WorktreeCreateFailed, result.ErrorMessage)).Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.WorktreeCreateSuccess)}");
        response.AppendLine();
        response.AppendLine(L.T(StringKey.WorktreeLabelAgentId, agent_id));
        response.AppendLine(L.T(StringKey.WorktreeLabelPath, result.Session!.WorktreePath));
        response.AppendLine(L.T(StringKey.WorktreeLabelBranch, result.Session.BranchName));

        if (!string.IsNullOrEmpty(result.Session.BaseCommitSha))
        {
            response.AppendLine(L.T(StringKey.WorktreeLabelBaseCommit, result.Session.BaseCommitSha[..Math.Min(8, result.Session.BaseCommitSha.Length)]));
        }

        response.AppendLine();
        response.AppendLine(L.T(StringKey.WorktreeAgentIsolationNote));

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 移除代理Worktree
    /// </summary>
    [McpTool(WorktreeToolNameConstants.WorktreeRemove, "Remove an agent's Git Worktree", "worktree")]
    public async Task<ToolResult> WorktreeRemoveAsync(
        [McpToolParameter("Agent ID")] string agent_id,
        [McpToolParameter("Force remove (even with uncommitted changes)", Required = false, DefaultValue = "false")] bool? force = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_id))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.WorktreeAgentIdCannotBeEmpty)).Build();
        }

        // 先检查是否有未提交更改
        var session = await _worktreeService.GetSessionAsync(agent_id);
        if (session != null)
        {
            var hasChanges = await _worktreeService.HasUncommittedChangesAsync(session.WorktreePath, cancellationToken);
            if (hasChanges && force != true)
            {
                return McpResultBuilder.Error()
                    .WithText(L.T(StringKey.WorktreeUncommittedChangesWarning))
                    .Build();
            }
        }

        var result = await _worktreeService.RemoveAgentWorktreeAsync(agent_id, force ?? false, cancellationToken);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.WorktreeRemoveFailed, result.ErrorMessage)).Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.WorktreeRemoved)}");
        response.AppendLine();
        response.AppendLine(L.T(StringKey.WorktreeLabelAgentId, agent_id));

        if (result.Forced)
        {
            response.AppendLine($"{StatusSymbol.Warning.ToValue()} {L.T(StringKey.WorktreeForceRemoveNote)}");
        }

        if (!string.IsNullOrEmpty(result.BlockReason))
        {
            response.AppendLine(L.T(StringKey.WorktreeLabelBlockReason, result.BlockReason));
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 列出所有Worktree会话
    /// </summary>
    [McpTool(WorktreeToolNameConstants.WorktreeList, "List all active Worktree sessions", "worktree")]
    public async Task<ToolResult> WorktreeListAsync(
        CancellationToken cancellationToken = default)
    {
        var sessions = await _worktreeService.GetAllSessionsAsync(cancellationToken);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.Directory.ToValue()} {L.T(StringKey.WorktreeSessionList)}");
        response.AppendLine(L.T(StringKey.WorktreeActiveSessionCount, sessions.Count));
        response.AppendLine();

        if (sessions.Count == 0)
        {
            response.AppendLine(L.T(StringKey.WorktreeNoActiveSessions));
        }
        else
        {
            foreach (var session in sessions.OrderBy(s => s.CreatedAt))
            {
                response.AppendLine($"{ObjectSymbol.Directory.ToValue()} [{session.AgentId}]");
                response.AppendLine($"   {L.T(StringKey.WorktreeLabelPath, session.WorktreePath)}");
                response.AppendLine($"   {L.T(StringKey.WorktreeLabelBranch, session.BranchName)}");
                response.AppendLine($"   {L.T(StringKey.WorktreeLabelCreated, $"{session.CreatedAt:yyyy-MM-dd HH:mm:ss}")}");
                response.AppendLine();
            }
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 获取Worktree会话详情
    /// </summary>
    [McpTool(WorktreeToolNameConstants.WorktreeStatus, "Get detailed status of an agent's Worktree", "worktree")]
    public async Task<ToolResult> WorktreeStatusAsync(
        [McpToolParameter("Agent ID")] string agent_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_id))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.WorktreeAgentIdCannotBeEmpty)).Build();
        }

        var session = await _worktreeService.GetSessionAsync(agent_id);

        if (session == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.WorktreeSessionNotFound, agent_id)).Build();
        }

        // 检查状态
        var hasChanges = await _worktreeService.HasUncommittedChangesAsync(session.WorktreePath, cancellationToken);
        var hasUnpushed = await _worktreeService.HasUnpushedCommitsAsync(session.WorktreePath, session.BaseCommitSha, cancellationToken);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.List.ToValue()} {L.T(StringKey.WorktreeStatusLabel, agent_id)}");
        response.AppendLine();
        response.AppendLine(L.T(StringKey.WorktreeLabelPath, session.WorktreePath));
        response.AppendLine(L.T(StringKey.WorktreeLabelBranch, session.BranchName));
        response.AppendLine(L.T(StringKey.WorktreeLabelGitRoot, session.GitRootPath));

        if (!string.IsNullOrEmpty(session.BaseCommitSha))
        {
            response.AppendLine(L.T(StringKey.WorktreeLabelBaseCommit, session.BaseCommitSha[..Math.Min(8, session.BaseCommitSha.Length)]));
        }

        response.AppendLine();
        response.AppendLine(L.T(StringKey.WorktreeUncommittedChanges, hasChanges ? $"{StatusSymbol.Warning.ToValue()} {L.T(StringKey.WorktreeHasChanges)}" : $"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.WorktreeNoChanges)}"));
        response.AppendLine(L.T(StringKey.WorktreeUnpushedCommits, hasUnpushed ? $"{ObjectSymbol.ArrowUp.ToValue()} {L.T(StringKey.WorktreeHasUnpushed)}" : $"{StatusSymbol.Tick.ToValue()} {L.T(StringKey.WorktreeNoUnpushed)}"));
        response.AppendLine();
        response.AppendLine(L.T(StringKey.WorktreeLabelCreateTime, $"{session.CreatedAt:yyyy-MM-dd HH:mm:ss}"));

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 清理过期Worktree
    /// </summary>
    [McpTool(WorktreeToolNameConstants.WorktreeCleanup, "Clean up stale Worktree sessions", "worktree")]
    public async Task<ToolResult> WorktreeCleanupAsync(
        [McpToolParameter("Stale timeout in hours (default 24)", Required = false)] int? stale_hours = null,
        [McpToolParameter("Confirm execution (enter 'yes' to confirm)")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        if (confirm != "yes")
        {
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.WorktreeConfirmCleanup))
                .Build();
        }

        var options = new WorktreeOptions
        {
            StaleTimeoutHours = stale_hours ?? 24
        };

        var cleanedCount = await _worktreeService.CleanupStaleWorktreesAsync(options, cancellationToken);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.Clean.ToValue()} {L.T(StringKey.WorktreeCleanupComplete)}");
        response.AppendLine();
        response.AppendLine(L.T(StringKey.WorktreeLabelStaleHours, options.StaleTimeoutHours));
        response.AppendLine(L.T(StringKey.WorktreeLabelCleanedCount, cleanedCount));

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 查找Git仓库根目录
    /// </summary>
    [McpTool(WorktreeToolNameConstants.WorktreeFindGit, "Find the Git repository root directory for a given path", "worktree")]
    public async Task<ToolResult> WorktreeFindGitAsync(
        [McpToolParameter("Start path (optional, defaults to current directory)", Required = false)] string? start_path = null,
        CancellationToken cancellationToken = default)
    {
        var path = start_path ?? _fs.GetCurrentDirectory();
        var gitRoot = await _worktreeService.FindGitRootAsync(path);

        if (string.IsNullOrEmpty(gitRoot))
        {
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.WorktreeGitRootNotFound, path))
                .Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.Search.ToValue()} {L.T(StringKey.WorktreeGitRootTitle)}");
        response.AppendLine();
        response.AppendLine(L.T(StringKey.WorktreeLabelStartPath, path));
        response.AppendLine(L.T(StringKey.WorktreeLabelGitRootPath, gitRoot));

        // 列出Worktree
        var worktrees = await _worktreeService.ListWorktreesAsync(gitRoot, cancellationToken);

        if (worktrees.Count > 0)
        {
            response.AppendLine();
            response.AppendLine(L.T(StringKey.WorktreeExistingCount, worktrees.Count));
            response.Append(string.Join(Environment.NewLine, worktrees.Select(wt => $"  - {wt}")));
            response.AppendLine();
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 列出所有Worktree
    /// </summary>
    [McpTool(WorktreeToolNameConstants.WorktreeListAll, "List all Worktrees in a Git repository", "worktree")]
    public async Task<ToolResult> WorktreeListAllAsync(
        [McpToolParameter("Git repository root directory (optional, auto-detected)", Required = false)] string? git_root = null,
        CancellationToken cancellationToken = default)
    {
        var worktrees = await _worktreeService.ListWorktreesAsync(git_root, cancellationToken);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.Directory.ToValue()} {L.T(StringKey.WorktreeListTitle)}");
        response.AppendLine(L.T(StringKey.WorktreeTotalCount, worktrees.Count));
        response.AppendLine();

        if (worktrees.Count == 0)
        {
            response.AppendLine(L.T(StringKey.WorktreeNoWorktrees));
        }
        else
        {
            response.Append(string.Join(Environment.NewLine, worktrees.Select(wt =>
            {
                var isMain = !wt.Contains("worktrees");
                var icon = isMain ? StatusSymbol.Stop.ToValue() : ObjectSymbol.Directory.ToValue();
                return $"{icon} {wt}";
            })));
            response.AppendLine();
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }
}
