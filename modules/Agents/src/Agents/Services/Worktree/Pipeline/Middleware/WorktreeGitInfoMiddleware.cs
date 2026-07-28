namespace Core.Agents.Worktree;

/// <summary>
/// Worktree Git 信息获取中间件 — 获取当前分支、HEAD commit SHA，以及基础分支
/// 对齐 TS getOrCreateWorktree：本地已有 origin ref 时跳过 fetch（大仓库节省 6-8s）
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware))]
public sealed partial class WorktreeGitInfoMiddleware : IWorktreeCreateMiddleware
{
    [Inject] private readonly Lazy<IWorktreePipelineOperations> _worktreeService;
    [Inject] private readonly ILogger<WorktreeGitInfoMiddleware>? _logger;


    public async Task InvokeAsync(WorktreeCreateContext context, MiddlewareDelegate<WorktreeCreateContext> next, CancellationToken ct)
    {
        var gitRoot = context.GitRoot;

        context.OriginalBranch = await _worktreeService.Value.GetCurrentBranchAsync(gitRoot).ConfigureAwait(false);
        context.BaseCommitSha = await _worktreeService.Value.GetHeadCommitShaAsync(gitRoot).ConfigureAwait(false);

        var opts = context.Options ?? new WorktreeOptions();
        if (opts.PrNumber.HasValue)
        {
            var fetchResult = await _worktreeService.Value.ExecuteGitCommandAsync(
                gitRoot, $"fetch origin pull/{opts.PrNumber.Value}/head", ct).ConfigureAwait(false);

            if (fetchResult.Success)
            {
                context.BaseBranch = "FETCH_HEAD";
                var fetchHeadSha = await _worktreeService.Value.ResolveRefAsync(gitRoot, "FETCH_HEAD").ConfigureAwait(false);
                if (fetchHeadSha is not null)
                {
                    context.BaseCommitSha = fetchHeadSha;
                }
            }
            else
            {
                context.Fail($"无法 fetch PR #{opts.PrNumber.Value}: {fetchResult.Error}");
                return;
            }
        }
        else if (!string.IsNullOrEmpty(opts.BaseBranch))
        {
            var baseSha = await _worktreeService.Value.ResolveRefAsync(gitRoot, opts.BaseBranch).ConfigureAwait(false);
            if (baseSha is not null)
            {
                context.BaseCommitSha = baseSha;
                context.BaseBranch = opts.BaseBranch;
            }
        }
        else
        {
            var defaultBranch = await _worktreeService.Value.GetDefaultBranchAsync(gitRoot).ConfigureAwait(false);
            if (defaultBranch is not null)
            {
                var originRef = $"origin/{defaultBranch}";
                var originSha = await _worktreeService.Value.ResolveRefAsync(gitRoot, originRef).ConfigureAwait(false);

                if (originSha is not null)
                {
                    context.BaseBranch = originRef;
                    context.BaseCommitSha = originSha;
                    _logger?.LogDebug("本地已有 origin ref，跳过 fetch: {Ref} -> {Sha}", originRef, originSha);
                }
                else
                {
                    var fetchResult = await _worktreeService.Value.ExecuteGitCommandAsync(
                        gitRoot, $"fetch origin {defaultBranch}", ct).ConfigureAwait(false);

                    if (fetchResult.Success)
                    {
                        context.BaseBranch = originRef;
                        var fetchedSha = await _worktreeService.Value.ResolveRefAsync(gitRoot, originRef).ConfigureAwait(false);
                        if (fetchedSha is not null)
                        {
                            context.BaseCommitSha = fetchedSha;
                        }
                    }
                    else
                    {
                        context.BaseBranch = "HEAD";
                        _logger?.LogDebug("fetch origin {Branch} 失败，回退到 HEAD: {Error}", defaultBranch, fetchResult.Error);
                    }
                }
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
