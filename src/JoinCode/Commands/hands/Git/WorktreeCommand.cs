
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Worktree, Description = "管理智能体 Git Worktree", Usage = "/worktree [list|cleanup|remove|create|status] [options]", Category = ChatCommandCategory.Code)]
public sealed class WorktreeCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        if (context.Services.WorktreeService is not { } worktreeService)
        {
            if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Error}Worktree 服务未初始化{AnsiStyleConstants.Reset}");
            }
            return ChatCommandResult.Continue();
        }

        var args = ChatCommandBase.GetSplitArgs(context);
        var subCommand = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (subCommand)
        {
            case CrudActionConstants.List:
            case CrudActionConstants.Ls:
                await ListWorktreesAsync(context, worktreeService, args);
                break;
            case "cleanup" or "clean":
                await CleanupWorktreesAsync(context, worktreeService, args);
                break;
            case CrudActionConstants.Delete:
            case CrudActionConstants.Rm:
                await RemoveWorktreeAsync(context, worktreeService, args);
                break;
            case CrudActionConstants.Create:
                await CreateWorktreeAsync(context, worktreeService, args);
                break;
            case "status":
                await ShowWorktreeStatusAsync(context, worktreeService, args);
                break;
            default:
                TerminalHelper.WriteLine($"{TerminalColors.Error}未知子命令: {subCommand}{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine($"用法: {Usage}");
                break;
        }

        return ChatCommandResult.Continue();
    }

    private async Task ListWorktreesAsync(ChatCommandContext context, IAgentWorktreeService worktreeService, string[] args)
    {
        TerminalHelper.WriteLine("=== Worktree 列表 ===\n");

        var worktrees = await worktreeService.ListWorktreesAsync(null, context.CancellationToken);
        var sessions = await worktreeService.GetAllSessionsAsync(context.CancellationToken);

        if (worktrees.Count == 0)
        {
            TerminalHelper.WriteLine("没有找到任何 worktree");
            return;
        }

        var currentDir = context.Services.FileSystem.GetCurrentDirectory();

        foreach (var worktreePath in worktrees)
        {
            var isCurrent = worktreePath.Equals(currentDir, StringComparison.OrdinalIgnoreCase);
            var session = sessions.FirstOrDefault(s => s.WorktreePath.Equals(worktreePath, StringComparison.OrdinalIgnoreCase));

            var prefix = isCurrent ? "* " : "  ";
            TerminalHelper.WriteLine($"{prefix}{worktreePath}");

            if (session is not null)
            {
                TerminalHelper.WriteLine($"    智能体: {session.AgentId}");
                TerminalHelper.WriteLine($"    分支: {session.BranchName}");
                TerminalHelper.WriteLine($"    创建时间: {session.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                if (session.Existed)
                {
                    TerminalHelper.WriteLine($"{TerminalColors.Warning}    [恢复现有]{AnsiStyleConstants.Reset}");
                }
            }

            if (context.Services.FileSystem.DirectoryExists(worktreePath))
            {
                var hasChanges = await worktreeService.HasUncommittedChangesAsync(worktreePath, context.CancellationToken);
                if (hasChanges)
                {
                    TerminalHelper.WriteLine($"{TerminalColors.Warning}    [有未提交更改]{AnsiStyleConstants.Reset}");
                }
            }

            TerminalHelper.NewLine();
        }

        TerminalHelper.WriteLine($"总计: {worktrees.Count} 个 worktree");
    }

    private async Task CleanupWorktreesAsync(ChatCommandContext context, IAgentWorktreeService worktreeService, string[] args)
    {
        TerminalHelper.WriteLine("=== 清理过期 Worktree ===\n");

        var gitRoot = await worktreeService.FindGitRootAsync(context.Services.FileSystem.GetCurrentDirectory());
        if (string.IsNullOrEmpty(gitRoot))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}未找到 Git 仓库根目录{AnsiStyleConstants.Reset}");
            return;
        }

        var worktreesDir = WorkflowConstants.Paths.GetProjectWorktreesDir(gitRoot);
        var fs = context.Services.FileSystem;
        if (!fs.DirectoryExists(worktreesDir))
        {
            TerminalHelper.WriteLine("没有 worktree 需要清理");
            return;
        }

        var entries = fs.GetDirectories(worktreesDir, "*", SearchOption.TopDirectoryOnly);
        var staleWorktrees = new List<string>();

        foreach (var entry in entries)
        {
            var dirName = Path.GetFileName(entry);
            if (dirName.StartsWith("agent-"))
            {
                var lastWrite = fs.GetDirectoryLastWriteTimeUtc(entry);
                var daysOld = (DateTime.UtcNow - lastWrite).TotalDays;

                if (daysOld > 7)
                {
                    staleWorktrees.Add(entry);
                }
            }
        }

        if (staleWorktrees.Count == 0)
        {
            TerminalHelper.WriteLine("没有过期的 worktree 需要清理");
            return;
        }

        TerminalHelper.WriteLine($"发现 {staleWorktrees.Count} 个过期 worktree:");
        foreach (var wt in staleWorktrees)
        {
            TerminalHelper.WriteLine($"  - {wt}");
        }

        if (!(context.Confirm?.Invoke("\n确认清理这些 worktree 吗？") ?? false))
        {
            TerminalHelper.WriteLine("已取消清理");
            return;
        }

        var options = new WorktreeOptions { StaleTimeout = TimeSpan.FromDays(7) };
        var cleanedCount = await worktreeService.CleanupStaleWorktreesAsync(options, context.CancellationToken);

        TerminalHelper.WriteLine($"{TerminalColors.Success}\n成功清理 {cleanedCount} 个过期 worktree{AnsiStyleConstants.Reset}");
    }

    private async Task RemoveWorktreeAsync(ChatCommandContext context, IAgentWorktreeService worktreeService, string[] args)
    {
        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}请指定要移除的 Agent ID{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine("用法: /worktree remove <agent-id> [--force]");
            return;
        }

        var agentId = args[1];
        var force = args.Contains("--force") || args.Contains("-f");

        TerminalHelper.WriteLine("=== 移除 Worktree ===");
        TerminalHelper.WriteLine($"智能体: {agentId}");
        TerminalHelper.WriteLine($"强制模式: {(force ? "是" : "否")}\n");

        var session = await worktreeService.GetSessionAsync(agentId);
        if (session is null)
        {
            var gitRoot = await worktreeService.FindGitRootAsync(context.Services.FileSystem.GetCurrentDirectory());
            if (!string.IsNullOrEmpty(gitRoot))
            {
                var worktreePath = AgentWorktreeSession.GenerateWorktreePath(gitRoot, agentId);
                if (context.Services.FileSystem.DirectoryExists(worktreePath))
                {
                    TerminalHelper.WriteLine($"{TerminalColors.Warning}找到未记录的 worktree 目录: {worktreePath}{AnsiStyleConstants.Reset}");
                    if (context.Confirm?.Invoke("是否强制移除？") ?? false)
                    {
                        context.Services.FileSystem.DeleteDirectory(worktreePath, true);
                        TerminalHelper.WriteLine($"{TerminalColors.Success}已移除 worktree 目录{AnsiStyleConstants.Reset}");
                        return;
                    }
                }
            }

            TerminalHelper.WriteLine($"{TerminalColors.Error}未找到智能体 '{agentId}' 的 worktree{AnsiStyleConstants.Reset}");
            return;
        }

        if (!force)
        {
            var hasChanges = await worktreeService.HasUncommittedChangesAsync(session.WorktreePath, context.CancellationToken);
            if (hasChanges)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Warning}该 worktree 有未提交的更改{AnsiStyleConstants.Reset}");
                if (!(context.Confirm?.Invoke("是否强制移除？") ?? false))
                {
                    TerminalHelper.WriteLine("已取消移除");
                    return;
                }
                force = true;
            }
        }

        var result = await worktreeService.RemoveAgentWorktreeAsync(agentId, force, context.CancellationToken);

        if (result.Success)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Success}成功移除 worktree{(result.Forced ? " (强制)" : "")}{AnsiStyleConstants.Reset}");
        }
        else if (!string.IsNullOrEmpty(result.BlockReason))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}无法移除: {result.BlockReason}{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine("使用 --force 强制移除");
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}移除失败: {result.ErrorMessage}{AnsiStyleConstants.Reset}");
        }
    }

    private async Task CreateWorktreeAsync(ChatCommandContext context, IAgentWorktreeService worktreeService, string[] args)
    {
        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}请指定 Agent ID{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine("用法: /worktree create <agent-id>");
            return;
        }

        var agentId = args[1];

        TerminalHelper.WriteLine("=== 创建 Worktree ===");
        TerminalHelper.WriteLine($"智能体: {agentId}\n");

        var result = await worktreeService.CreateAgentWorktreeAsync(agentId, null, null, context.CancellationToken);

        if (result.Success)
        {
            if (result.Existed)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Success}恢复现有 worktree:{AnsiStyleConstants.Reset}");
            }
            else
            {
                TerminalHelper.WriteLine($"{TerminalColors.Success}成功创建 worktree:{AnsiStyleConstants.Reset}");
            }

            TerminalHelper.WriteLine($"  路径: {result.Session!.WorktreePath}");
            TerminalHelper.WriteLine($"  分支: {result.Session.BranchName}");
            TerminalHelper.WriteLine($"  Git根: {result.Session.GitRootPath}");

            if (result.Session.CreationDurationMs.HasValue)
            {
                TerminalHelper.WriteLine($"  耗时: {result.Session.CreationDurationMs}ms");
            }
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}创建失败: {result.ErrorMessage}{AnsiStyleConstants.Reset}");
        }
    }

    private async Task ShowWorktreeStatusAsync(ChatCommandContext context, IAgentWorktreeService worktreeService, string[] args)
    {
        var agentId = args.Length > 1 ? args[1] : null;

        TerminalHelper.WriteLine("=== Worktree 状态 ===\n");

        if (!string.IsNullOrEmpty(agentId))
        {
            var session = await worktreeService.GetSessionAsync(agentId);
            if (session is null)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Error}未找到智能体 '{agentId}' 的 worktree{AnsiStyleConstants.Reset}");
                return;
            }

            await ShowSessionStatusAsync(context, worktreeService, session);
        }
        else
        {
            var sessions = await worktreeService.GetAllSessionsAsync(context.CancellationToken);
            if (sessions.Count == 0)
            {
                TerminalHelper.WriteLine("没有活动的 worktree 会话");
                return;
            }

            foreach (var session in sessions)
            {
                TerminalHelper.WriteLine($"[{session.AgentId}]");
                await ShowSessionStatusAsync(context, worktreeService, session);
                TerminalHelper.NewLine();
            }
        }
    }

    private async Task ShowSessionStatusAsync(ChatCommandContext context, IAgentWorktreeService worktreeService, AgentWorktreeSession session)
    {
        TerminalHelper.WriteLine($"  Worktree: {session.WorktreePath}");
        TerminalHelper.WriteLine($"  分支: {session.BranchName}");
        TerminalHelper.WriteLine($"  原始目录: {session.OriginalCwd}");
        TerminalHelper.WriteLine($"  创建时间: {session.CreatedAt:yyyy-MM-dd HH:mm:ss}");

        if (context.Services.FileSystem.DirectoryExists(session.WorktreePath))
        {
            var hasChanges = await worktreeService.HasUncommittedChangesAsync(session.WorktreePath, context.CancellationToken);
            TerminalHelper.WriteLine($"  未提交更改: {(hasChanges ? "是" : "否")}");

            var hasUnpushed = await worktreeService.HasUnpushedCommitsAsync(session.WorktreePath, session.BaseCommitSha, context.CancellationToken);
            TerminalHelper.WriteLine($"  未推送提交: {(hasUnpushed ? "是" : "否")}");
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}  [目录不存在]{AnsiStyleConstants.Reset}");
        }
    }
}
