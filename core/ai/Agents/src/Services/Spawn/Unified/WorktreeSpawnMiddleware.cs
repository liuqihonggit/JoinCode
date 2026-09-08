namespace Core.Agents;

/// <summary>
/// Worktree 创建中间件 — 合并路径 A 的 AgentWorktreeSpawn + 路径 B 的 SpawnCoordWorktree
/// 统一降级策略：失败记日志警告并继续（不抛 [AGT011]）
/// 主代理 no-op
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorktreeSpawnMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    public WorktreeSpawnMiddleware(
        IAgentWorktreeService? worktreeService = null,
        IAgentWorktreeManager? worktreeManager = null,
        ILogger<WorktreeSpawnMiddleware>? logger = null)
    {
        _worktreeService = worktreeService;
        _worktreeManager = worktreeManager;
        _logger = logger;
    }
    private readonly IAgentWorktreeService? _worktreeService;
    private readonly IAgentWorktreeManager? _worktreeManager;
    private readonly ILogger<WorktreeSpawnMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct)
    {
        if (!context.IsMainAgent && context.Agent is not null)
        {
            var agentId = context.AgentId;
            var worktreeCreated = false;

            if (context.SpawnOptions?.IsolationMode == AgentIsolationMode.Worktree && _worktreeService is not null)
            {
                worktreeCreated = await CreatePerAgentWorktreeAsync(context, agentId, ct).ConfigureAwait(false);
            }
            else if (_worktreeManager is not null && _worktreeManager.IsWorktreeIsolationEnabled)
            {
                worktreeCreated = await CreateGlobalWorktreeAsync(context, agentId, ct).ConfigureAwait(false);
            }

            if (!worktreeCreated)
            {
                var agent = (AgentBase)context.Agent!;
                agent.AddContext("当前选择不创建 worktree，在主工作目录中执行。");
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task<bool> CreatePerAgentWorktreeAsync(UnifiedSpawnContext context, string agentId, CancellationToken ct)
    {
        _logger?.LogInformation("[WorktreeSpawn] 为 Agent {AgentId} 创建隔离 Worktree (per-agent)", agentId);

        try
        {
            AgentWorktreeSession? session = null;
            if (_worktreeManager is not null)
            {
                session = await _worktreeManager.CreateWorktreeForAgentAsync(agentId, ct).ConfigureAwait(false);
            }
            else if (_worktreeService is not null)
            {
                var result = await _worktreeService.CreateAgentWorktreeAsync(agentId, cancellationToken: ct).ConfigureAwait(false);
                session = result.Success ? result.Session : null;
            }

            if (session is null)
            {
                _logger?.LogWarning("[WorktreeSpawn] 创建 Worktree 失败，降级为普通模式");
                return false;
            }

            var worktreePath = session.WorktreePath;
            _logger?.LogInformation("[WorktreeSpawn] Agent {AgentId} Worktree 创建成功: {Path}", agentId, worktreePath);

            var agent = (AgentBase)context.Agent!;
            agent.Options.WorktreePath = worktreePath;
            agent.Options.WorktreeBranch = session.BranchName;

            if (agent.Context is not null)
            {
                agent.Context.WorktreePath = worktreePath;
            }

            agent.AddContext($"已创建git worktree,路径是:{worktreePath} 已创建git分支:{session.BranchName}");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[WorktreeSpawn] 创建 Worktree 异常: {Error}，降级为普通模式", ex.Message);
            return false;
        }
    }

    private async Task<bool> CreateGlobalWorktreeAsync(UnifiedSpawnContext context, string agentId, CancellationToken ct)
    {
        _logger?.LogInformation("[WorktreeSpawn] 为 Agent {AgentId} 创建 Worktree (全局隔离)", agentId);

        try
        {
            var worktreeCreated = await _worktreeManager!.CreateWorktreeAsync(agentId, ct).ConfigureAwait(false);
            if (!worktreeCreated)
            {
                _logger?.LogWarning("[WorktreeSpawn] 创建 Worktree 失败，降级为普通模式 (原 [AGT011] 硬失败改为降级)");
                return false;
            }
            context.WorktreeCreated = true;

            var session = await _worktreeManager.GetWorktreeSessionAsync(agentId, ct).ConfigureAwait(false);
            if (session is not null)
            {
                var agent = (AgentBase)context.Agent!;
                agent.AddContext($"已创建git worktree,路径是:{session.WorktreePath} 已创建git分支:{session.BranchName}");
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[WorktreeSpawn] 创建 Worktree 异常: {Error}，降级为普通模式", ex.Message);
            return false;
        }
    }
}
