namespace Infrastructure.HotSpot;

/// <summary>
/// 自动 rebase 同步服务实现 — SubagentStop 时系统自动执行 git fetch + rebase
/// <para>
/// 状态机驱动：Idle → Fetching → CheckingUpstream → (StashingDirty) → Rebasing → Completed/ConflictDetected → Aborting → Completed
/// 守卫：fetch 失败即 Failed（涵盖 worktree 不存在）；stash 失败即 Failed；rebase 冲突 abort + 邮箱通知
/// </para>
/// </summary>
[Register(typeof(IAutoRebaseService), ServiceLifetime.Singleton)]
public sealed class AutoRebaseService : IAutoRebaseService
{
    private readonly IGitCommandRunner _gitRunner;
    private readonly IMailbox _mailbox;
    private readonly ILogger<AutoRebaseService>? _logger;

    public AutoRebaseService(IGitCommandRunner gitRunner, IMailbox mailbox, ILogger<AutoRebaseService>? logger = null)
    {
        _gitRunner = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));
        _mailbox = mailbox ?? throw new ArgumentNullException(nameof(mailbox));
        _logger = logger;
    }

    public async Task<AutoRebaseResult> RebaseSyncAsync(AutoRebaseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorktreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AgentId);

        var upstream = request.UpstreamBranch;
        var workDir = request.WorktreePath;
        var fetch = GitSubCommand.Fetch.ToValue();
        var rebase = GitSubCommand.Rebase.ToValue();
        var stash = GitSubCommand.Stash.ToValue();

        var fetchResult = await _gitRunner.ExecuteAsync($"{fetch} {upstream}", workDir, cancellationToken).ConfigureAwait(false);
        if (!fetchResult.Success)
        {
            _logger?.LogWarning("[AutoRebase] fetch 失败 for {AgentId}: {Error}", request.AgentId, fetchResult.Error);
            return Failed($"fetch 失败: {fetchResult.Error}");
        }

        var upstreamCount = await _gitRunner.GetUpstreamCommitCountAsync(upstream, workDir, cancellationToken).ConfigureAwait(false);
        if (upstreamCount < 0)
        {
            _logger?.LogWarning("[AutoRebase] rev-list 解析失败 for {AgentId}", request.AgentId);
            return Failed("rev-list 解析失败");
        }
        if (upstreamCount == 0)
        {
            _logger?.LogDebug("[AutoRebase] 主干无新提交，跳过 for {AgentId}", request.AgentId);
            return Skipped("主干无新提交");
        }

        var hasUncommitted = await _gitRunner.HasUncommittedChangesAsync(workDir, cancellationToken).ConfigureAwait(false);

        var stashed = false;
        if (hasUncommitted)
        {
            var stashResult = await _gitRunner.ExecuteAsync($"{stash} push -m \"auto-rebase-stash\"", workDir, cancellationToken).ConfigureAwait(false);
            if (!stashResult.Success)
            {
                _logger?.LogWarning("[AutoRebase] stash 失败 for {AgentId}: {Error}", request.AgentId, stashResult.Error);
                return Failed($"stash 失败: {stashResult.Error}");
            }
            stashed = true;
        }

        var rebaseResult = await _gitRunner.ExecuteAsync($"{rebase} {upstream}", workDir, cancellationToken).ConfigureAwait(false);

        if (rebaseResult.Success)
        {
            if (stashed)
                await SafeStashPopAsync(workDir, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("[AutoRebase] rebase 成功 for {AgentId}", request.AgentId);
            return new AutoRebaseResult
            {
                FinalState = RebaseSyncState.Completed,
                Success = true,
                Message = "rebase 成功",
            };
        }

        var conflictFiles = await _gitRunner.GetConflictFilesAsync(workDir, cancellationToken).ConfigureAwait(false);

        await _gitRunner.ExecuteAsync($"{rebase} --abort", workDir, cancellationToken).ConfigureAwait(false);

        if (stashed)
            await SafeStashPopAsync(workDir, cancellationToken).ConfigureAwait(false);

        if (request.CaptainId is not null)
            await NotifyConflictAsync(request, conflictFiles, cancellationToken).ConfigureAwait(false);

        _logger?.LogWarning("[AutoRebase] rebase 冲突 for {AgentId}, 冲突文件: {Files}", request.AgentId, string.Join(", ", conflictFiles));

        return new AutoRebaseResult
        {
            FinalState = RebaseSyncState.Completed,
            Success = true,
            Message = "rebase 冲突已 abort，邮箱已通知队长",
            ConflictFiles = conflictFiles,
            HadConflicts = true,
        };
    }

    private async Task SafeStashPopAsync(string workDir, CancellationToken ct)
    {
        try
        {
            await _gitRunner.ExecuteAsync($"{GitSubCommand.Stash.ToValue()} pop", workDir, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("[AutoRebase] stash pop 异常: {Error}", ex.Message);
        }
    }

    private async Task NotifyConflictAsync(AutoRebaseRequest request, IReadOnlyList<string> conflictFiles, CancellationToken ct)
    {
        var filesText = conflictFiles.Count > 0 ? string.Join("\n", conflictFiles) : "(未知)";
        var msg = new CoordinatorMessage
        {
            FromAgentId = request.AgentId,
            ToAgentId = request.CaptainId!,
            MessageType = TeammateMessageTypeConstants.ForceSync,
            Content = FormattableString.Invariant($"Worker {request.AgentId} rebase {request.UpstreamBranch} 产生冲突，已 abort。冲突文件:\n{filesText}\n请处理冲突后重新派发任务。"),
            StructuredType = TeammateMessageType.ForceSync,
        };
        await _mailbox.SendAsync(request.CaptainId!, msg, ct).ConfigureAwait(false);
    }

    private static AutoRebaseResult Failed(string message) => new()
    {
        FinalState = RebaseSyncState.Failed,
        Success = false,
        Message = message,
    };

    private static AutoRebaseResult Skipped(string message) => new()
    {
        FinalState = RebaseSyncState.Skipped,
        Success = true,
        Message = message,
        WasSkipped = true,
    };
}
