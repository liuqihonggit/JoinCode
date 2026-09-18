namespace Core.Agents.Worktree;

/// <summary>
/// Worktree Git 信息获取中间件 — 获取当前分支、HEAD commit SHA，以及基础分支
/// 对齐 TS getOrCreateWorktree：本地已有 origin ref 时跳过 fetch（大仓库节省 6-8s）
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorktreeGitInfoMiddleware : ServiceEntity, IWorktreeCreateMiddleware
{

    /// <summary>
    /// 构造 WorktreeGitInfoMiddleware 实例，注入延迟加载的管道操作、文件操作服务及日志器
    /// </summary>
    public WorktreeGitInfoMiddleware(Lazy<IWorktreePipelineOperations> worktreeService, IFileOperationService fs, ILogger<WorktreeGitInfoMiddleware>? logger = null)
    {
        _worktreeService = worktreeService;
        _fs = fs;
        _logger = logger;
    }
    private readonly Lazy<IWorktreePipelineOperations> _worktreeService;
    private readonly IFileOperationService _fs;
    private readonly ILogger<WorktreeGitInfoMiddleware>? _logger;


    /// <summary>执行优先级:Git 信息获取在恢复检查之后</summary>
    public int Order => 400;

    /// <summary>
    /// 执行 Git 信息获取：获取当前分支、HEAD commit SHA，并按 PR 号/基准分支/默认分支解析基础引用
    /// </summary>
    /// <param name="context">worktree 创建上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(WorktreeCreateContext context, MiddlewareDelegate<WorktreeCreateContext> next, CancellationToken ct)
    {
        WorktreeContextEnricher.EnsureGitRoot(context, _fs);
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
