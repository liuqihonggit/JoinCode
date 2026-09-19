
namespace JoinCode.ChatCommands;

/// <summary>
/// /worktree 命令 — 管理智能体 Git Worktree，支持 list/cleanup/remove/create/status 操作
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Worktree, Description = "管理智能体 Git Worktree", Usage = "/worktree [list|cleanup|remove|create|status] [options]", Category = ChatCommandCategory.Code)]
[ChatCommandArg("action", Type = "string", Description = "Worktree 操作", Enum = new[] { "list", "cleanup", "remove", "create", "status" })]
[ChatCommandArg("options", Type = "string", Description = "操作特定参数,如 create 的分支名")]
public sealed class WorktreeCommand : ChatCommandBase {
    /// <summary>
    /// 异步执行 /worktree 命令
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>命令执行结果</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        if (context.GetCommandServices().WorktreeService is not { } worktreeService) {
            if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive) {
                TerminalHelper.WriteLine($"{TerminalColors.Error}Worktree 服务未初始化{AnsiStyleEnumConstants.Reset}");
            }
            return ChatCommandResult.Continue();
        }

        var args = ChatCommandBase.GetSplitArgs(context);
        var subCommand = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (subCommand) {
            case CrudActionEnumConstants.List:
            case CrudActionEnumConstants.Ls:
            await ListWorktreesAsync(context, worktreeService, args).ConfigureAwait(false);
            break;
            case "cleanup" or "clean":
            await CleanupWorktreesAsync(context, worktreeService, args).ConfigureAwait(false);
            break;
            case CrudActionEnumConstants.Delete:
            case CrudActionEnumConstants.Rm:
            await RemoveWorktreeAsync(context, worktreeService, args).ConfigureAwait(false);
            break;
            case CrudActionEnumConstants.Create:
            await CreateWorktreeAsync(context, worktreeService, args).ConfigureAwait(false);
            break;
            case "status":
            await ShowWorktreeStatusAsync(context, worktreeService, args).ConfigureAwait(false);
            break;
            default:
            TerminalHelper.WriteLine($"{TerminalColors.Error}未知子命令: {subCommand}{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine($"用法: {Usage}");
            break;
        }

        return ChatCommandResult.Continue();
    }

    private async Task ListWorktreesAsync(ChatCommandContext context, IAgentWorktreeService worktreeService, string[] args) {
        TerminalHelper.WriteLine("=== Worktree 列表 ===\n");

        var worktrees = await worktreeService.ListWorktreesAsync(null, context.CancellationToken).ConfigureAwait(false);
        var sessions = await worktreeService.GetAllSessionsAsync(context.CancellationToken).ConfigureAwait(false);

        if (worktrees.Count == 0) {
            TerminalHelper.WriteLine("没有找到任何 worktree");
            return;
        }

        var currentDir = context.GetCommandServices().FileSystem.GetCurrentDirectory();
        var sessionByPath = sessions.ToLookup(s => s.WorktreePath, StringComparer.OrdinalIgnoreCase);

        foreach (var worktreePath in worktrees) {
            var isCurrent = worktreePath.Equals(currentDir, StringComparison.OrdinalIgnoreCase);
            var session = sessionByPath[worktreePath].FirstOrDefault();

            var prefix = isCurrent ? "* " : "  ";
            TerminalHelper.WriteLine($"{prefix}{worktreePath}");

            if (session is not null) {
                TerminalHelper.WriteLine($"    智能体: {session.AgentId}");
                TerminalHelper.WriteLine($"    分支: {session.BranchName}");
                TerminalHelper.WriteLine($"    创建时间: {session.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                if (session.Existed) {
                    TerminalHelper.WriteLine($"{TerminalColors.Warning}    [恢复现有]{AnsiStyleEnumConstants.Reset}");
                }
            }

            if (context.GetCommandServices().FileSystem.DirectoryExists(worktreePath)) {
                var hasChanges = await worktreeService.HasUncommittedChangesAsync(worktreePath, context.CancellationToken).ConfigureAwait(false);
                if (hasChanges) {
                    TerminalHelper.WriteLine($"{TerminalColors.Warning}    [有未提交更改]{AnsiStyleEnumConstants.Reset}");
                }
            }

            TerminalHelper.NewLine();
        }

        TerminalHelper.WriteLine($"总计: {worktrees.Count} 个 worktree");
    }

    private async Task CleanupWorktreesAsync(ChatCommandContext context, IAgentWorktreeService worktreeService, string[] args) {
        TerminalHelper.WriteLine("=== 清理过期 Worktree ===\n");

        var gitRoot = await worktreeService.FindGitRootAsync(context.GetCommandServices().FileSystem.GetCurrentDirectory()).ConfigureAwait(false);
        if (string.IsNullOrEmpty(gitRoot)) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}未找到 Git 仓库根目录{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var worktreesDir = WorkflowConstants.Paths.GetProjectWorktreesDir(gitRoot);
        var fs = context.GetCommandServices().FileSystem;
        if (!fs.DirectoryExists(worktreesDir)) {
            TerminalHelper.WriteLine("没有 worktree 需要清理");
            return;
        }

        var entries = fs.GetDirectories(worktreesDir, "*", SearchOption.TopDirectoryOnly);
        var staleWorktrees = new List<string>();

        foreach (var entry in entries) {
            var dirName = Path.GetFileName(entry);
            if (dirName.StartsWith("agent-")) {
                var lastWrite = fs.GetDirectoryLastWriteTimeUtc(entry);
                var daysOld = (DateTime.UtcNow - lastWrite).TotalDays;

                if (daysOld > 7) {
                    staleWorktrees.Add(entry);
                }
            }
        }

        if (staleWorktrees.Count == 0) {
            TerminalHelper.WriteLine("没有过期的 worktree 需要清理");
            return;
        }

        TerminalHelper.WriteLine($"发现 {staleWorktrees.Count} 个过期 worktree:");
        foreach (var wt in staleWorktrees) {
            TerminalHelper.WriteLine($"  - {wt}");
        }

        if (!(context.Confirm?.Invoke("\n确认清理这些 worktree 吗？") ?? false)) {
            TerminalHelper.WriteLine("已取消清理");
            return;
        }

        var options = new WorktreeOptions { StaleTimeout = TimeSpan.FromDays(7) };
        var cleanedCount = await worktreeService.CleanupStaleWorktreesAsync(options, context.CancellationToken).ConfigureAwait(false);

        TerminalHelper.WriteLine($"{TerminalColors.Success}\n成功清理 {cleanedCount} 个过期 worktree{AnsiStyleEnumConstants.Reset}");
    }

    private async Task RemoveWorktreeAsync(ChatCommandContext context, IAgentWorktreeService worktreeService, string[] args) {
        if (args.Length < 2) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}请指定要移除的 Agent ID{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine("用法: /worktree remove <agent-id> [--force]");
            return;
        }

        var agentId = args[1];
        var force = args.Contains(JccCliArgEnumConstants.Force) || args.Contains(JccCliArgEnumConstants.ForceAlias__f);

        TerminalHelper.WriteLine("=== 移除 Worktree ===");
        TerminalHelper.WriteLine($"智能体: {agentId}");
        TerminalHelper.WriteLine($"强制模式: {(force ? "是" : "否")}\n");

        var session = await worktreeService.GetSessionAsync(agentId).ConfigureAwait(false);
        if (session is null) {
            var gitRoot = await worktreeService.FindGitRootAsync(context.GetCommandServices().FileSystem.GetCurrentDirectory()).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(gitRoot)) {
                var worktreePath = AgentWorktreeSession.GenerateWorktreePath(gitRoot, agentId);
                if (context.GetCommandServices().FileSystem.DirectoryExists(worktreePath)) {
                    TerminalHelper.WriteLine($"{TerminalColors.Warning}找到未记录的 worktree 目录: {worktreePath}{AnsiStyleEnumConstants.Reset}");
                    if (context.Confirm?.Invoke("是否强制移除？") ?? false) {
                        // 兜底清理:session 不存在(未记录的 worktree),直接删除目录。
                        // 注意:可能残留 .git/worktrees/ 元数据和分支引用,建议后续执行 `git worktree prune`。
                        context.GetCommandServices().FileSystem.DeleteDirectory(worktreePath, true);
                        TerminalHelper.WriteLine($"{TerminalColors.Success}已移除 worktree 目录{AnsiStyleEnumConstants.Reset}");
                        return;
                    }
                }
            }

            TerminalHelper.WriteLine($"{TerminalColors.Error}未找到智能体 '{agentId}' 的 worktree{AnsiStyleEnumConstants.Reset}");
            return;
        }

        if (!force) {
            var hasChanges = await worktreeService.HasUncommittedChangesAsync(session.WorktreePath, context.CancellationToken).ConfigureAwait(false);
            if (hasChanges) {
                TerminalHelper.WriteLine($"{TerminalColors.Warning}该 worktree 有未提交的更改{AnsiStyleEnumConstants.Reset}");
                if (!(context.Confirm?.Invoke("是否强制移除？") ?? false)) {
                    TerminalHelper.WriteLine("已取消移除");
                    return;
                }
                force = true;
            }
        }

        var result = await worktreeService.RemoveAgentWorktreeAsync(agentId, force, context.CancellationToken).ConfigureAwait(false);

        if (result.Success) {
            TerminalHelper.WriteLine($"{TerminalColors.Success}成功移除 worktree{(result.Forced ? " (强制)" : "")}{AnsiStyleEnumConstants.Reset}");
        } else if (!string.IsNullOrEmpty(result.BlockReason)) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}无法移除: {result.BlockReason}{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine("使用 --force 强制移除");
        } else {
            TerminalHelper.WriteLine($"{TerminalColors.Error}移除失败: {result.ErrorMessage}{AnsiStyleEnumConstants.Reset}");
        }
    }

    private async Task CreateWorktreeAsync(ChatCommandContext context, IAgentWorktreeService worktreeService, string[] args) {
        if (args.Length < 2) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}请指定 Agent ID{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine("用法: /worktree create <agent-id>");
            return;
        }

        var agentId = args[1];

        TerminalHelper.WriteLine("=== 创建 Worktree ===");
        TerminalHelper.WriteLine($"智能体: {agentId}\n");

        var result = await worktreeService.CreateAgentWorktreeAsync(agentId, null, null, context.CancellationToken).ConfigureAwait(false);

        if (result.Success) {
            if (result.Existed) {
                TerminalHelper.WriteLine($"{TerminalColors.Success}恢复现有 worktree:{AnsiStyleEnumConstants.Reset}");
            } else {
                TerminalHelper.WriteLine($"{TerminalColors.Success}成功创建 worktree:{AnsiStyleEnumConstants.Reset}");
            }

            TerminalHelper.WriteLine($"  路径: {result.Session!.WorktreePath}");
            TerminalHelper.WriteLine($"  分支: {result.Session.BranchName}");
            TerminalHelper.WriteLine($"  Git根: {result.Session.GitRootPath}");

            if (result.Session.CreationDurationMs.HasValue) {
                TerminalHelper.WriteLine($"  耗时: {result.Session.CreationDurationMs}ms");
            }
        } else {
            TerminalHelper.WriteLine($"{TerminalColors.Error}创建失败: {result.ErrorMessage}{AnsiStyleEnumConstants.Reset}");
        }
    }

    private async Task ShowWorktreeStatusAsync(ChatCommandContext context, IAgentWorktreeService worktreeService, string[] args) {
        var agentId = args.Length > 1 ? args[1] : null;

        TerminalHelper.WriteLine("=== Worktree 状态 ===\n");

        if (!string.IsNullOrEmpty(agentId)) {
            var session = await worktreeService.GetSessionAsync(agentId).ConfigureAwait(false);
            if (session is null) {
                TerminalHelper.WriteLine($"{TerminalColors.Error}未找到智能体 '{agentId}' 的 worktree{AnsiStyleEnumConstants.Reset}");
                return;
            }

            await ShowSessionStatusAsync(context, worktreeService, session).ConfigureAwait(false);
        } else {
            var sessions = await worktreeService.GetAllSessionsAsync(context.CancellationToken).ConfigureAwait(false);
            if (!sessions.Any()) {
                TerminalHelper.WriteLine("没有活动的 worktree 会话");
                return;
            }

            foreach (var session in sessions) {
                TerminalHelper.WriteLine($"[{session.AgentId}]");
                await ShowSessionStatusAsync(context, worktreeService, session).ConfigureAwait(false);
                TerminalHelper.NewLine();
            }
        }
    }

    private async Task ShowSessionStatusAsync(ChatCommandContext context, IAgentWorktreeService worktreeService, AgentWorktreeSession session) {
        TerminalHelper.WriteLine($"  Worktree: {session.WorktreePath}");
        TerminalHelper.WriteLine($"  分支: {session.BranchName}");
        TerminalHelper.WriteLine($"  原始目录: {session.OriginalCwd}");
        TerminalHelper.WriteLine($"  创建时间: {session.CreatedAt:yyyy-MM-dd HH:mm:ss}");

        if (context.GetCommandServices().FileSystem.DirectoryExists(session.WorktreePath)) {
            var hasChanges = await worktreeService.HasUncommittedChangesAsync(session.WorktreePath, context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine($"  未提交更改: {(hasChanges ? "是" : "否")}");

            var hasUnpushed = await worktreeService.HasUnpushedCommitsAsync(session.WorktreePath, session.BaseCommitSha, context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine($"  未推送提交: {(hasUnpushed ? "是" : "否")}");
        } else {
            TerminalHelper.WriteLine($"{TerminalColors.Error}  [目录不存在]{AnsiStyleEnumConstants.Reset}");
        }
    }
}