namespace Core.Agents;

/// <summary>
/// Worktree 创建中间件 — 合并路径 A 的 AgentWorktreeSpawn + 路径 B 的 SpawnCoordWorktree
/// 统一降级策略：失败记日志警告并继续（不抛 [AGT011]）
/// 主代理 no-op
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware))]
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

            if (context.SpawnOptions?.IsolationMode == AgentIsolationMode.Worktree && _worktreeService is not null)
            {
                await CreatePerAgentWorktreeAsync(context, agentId, ct).ConfigureAwait(false);
            }
            else if (_worktreeManager is not null && _worktreeManager.IsWorktreeIsolationEnabled)
            {
                await CreateGlobalWorktreeAsync(context, agentId, ct).ConfigureAwait(false);
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task CreatePerAgentWorktreeAsync(UnifiedSpawnContext context, string agentId, CancellationToken ct)
    {
        _logger?.LogInformation("[WorktreeSpawn] 为 Agent {AgentId} 创建隔离 Worktree (per-agent)", agentId);

        try
        {
            var result = await _worktreeService!.CreateAgentWorktreeAsync(agentId, cancellationToken: ct).ConfigureAwait(false);

            if (!result.Success || result.Session is null)
            {
                _logger?.LogWarning("[WorktreeSpawn] 创建 Worktree 失败: {Error}，降级为普通模式", result.ErrorMessage);
                return;
            }

            var worktreePath = result.Session.WorktreePath;
            _logger?.LogInformation("[WorktreeSpawn] Agent {AgentId} Worktree 创建成功: {Path}", agentId, worktreePath);

            var agent = (AgentBase)context.Agent!;
            agent.Options.WorktreePath = worktreePath;
            agent.Options.WorktreeBranch = result.Session.BranchName;

            if (agent.Context is not null)
            {
                agent.Context.WorktreePath = worktreePath;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[WorktreeSpawn] 创建 Worktree 异常: {Error}，降级为普通模式", ex.Message);
        }
    }

    private async Task CreateGlobalWorktreeAsync(UnifiedSpawnContext context, string agentId, CancellationToken ct)
    {
        _logger?.LogInformation("[WorktreeSpawn] 为 Agent {AgentId} 创建 Worktree (全局隔离)", agentId);

        try
        {
            var worktreeCreated = await _worktreeManager!.CreateWorktreeAsync(agentId, ct).ConfigureAwait(false);
            if (!worktreeCreated)
            {
                _logger?.LogWarning("[WorktreeSpawn] 创建 Worktree 失败，降级为普通模式 (原 [AGT011] 硬失败改为降级)");
                return;
            }
            context.WorktreeCreated = true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[WorktreeSpawn] 创建 Worktree 异常: {Error}，降级为普通模式", ex.Message);
        }
    }
}
