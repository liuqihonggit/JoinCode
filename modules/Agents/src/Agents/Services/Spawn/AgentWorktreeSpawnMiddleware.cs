namespace Core.Agents;

/// <summary>
/// Worktree 创建中间件 — 当 IsolationMode = Worktree 时，为子智能体创建隔离的 Git Worktree
/// 对齐 TS: isolation: "worktree" — 在临时 git 工作树中运行代理
/// 在 ContextSetupMiddleware (Order=300) 创建 SubAgent 之后执行
/// </summary>
[Register]
public sealed partial class AgentWorktreeSpawnMiddleware : IAgentSpawnMiddleware
{
    [Inject] private readonly IAgentWorktreeService? _worktreeService;
    [Inject] private readonly ILogger<AgentWorktreeSpawnMiddleware>? _logger;

    /// <summary>Worktree 创建在上下文创建之后</summary>

    /// <summary>Worktree 创建失败不应中断管道（降级为普通模式）</summary>
    public JoinCode.Abstractions.Pipeline.ErrorBehavior OnError => JoinCode.Abstractions.Pipeline.ErrorBehavior.Continue;

    public async Task InvokeAsync(AgentSpawnContext context, JoinCode.Abstractions.Pipeline.MiddlewareDelegate<AgentSpawnContext> next, CancellationToken ct)
    {
        // 只在 IsolationMode == Worktree 且 SubAgent 已创建时执行
        if (context.Options.IsolationMode != AgentIsolationMode.Worktree
            || context.SubAgent is null
            || _worktreeService is null)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var agentId = context.SubAgent.Id;
        _logger?.LogInformation("[AgentWorktreeSpawn] 为 Agent {AgentId} 创建隔离 Worktree", agentId);

        try
        {
            var result = await _worktreeService.CreateAgentWorktreeAsync(agentId, cancellationToken: ct).ConfigureAwait(false);

            if (!result.Success || result.Session is null)
            {
                _logger?.LogWarning("[AgentWorktreeSpawn] 创建 Worktree 失败: {Error}，降级为普通模式", result.ErrorMessage);
                await next(context, ct).ConfigureAwait(false);
                return;
            }

            var worktreePath = result.Session.WorktreePath;
            _logger?.LogInformation("[AgentWorktreeSpawn] Agent {AgentId} Worktree 创建成功: {Path}", agentId, worktreePath);

            // 设置 SubAgent 的 WorktreePath，使其在 ExecuteAsync 中使用 worktree 作为 CWD
            context.SubAgent.Options.WorktreePath = worktreePath;
            context.SubAgent.Options.WorktreeBranch = result.Session.BranchName;

            // 同步设置 Context.WorktreePath，供下游中间件和转录使用
            if (context.SubAgent.Context is not null)
            {
                context.SubAgent.Context.WorktreePath = worktreePath;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[AgentWorktreeSpawn] 创建 Worktree 异常: {Error}，降级为普通模式", ex.Message);
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
