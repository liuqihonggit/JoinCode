namespace Core.Agents.Coordinator;

/// <summary>
/// SubagentStop Hook 执行器 — 在 Agent 释放前触发停止 Hook 与自动 rebase
/// </summary>
internal sealed class SubagentStopHookRunner
{
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly ISubagentStopHookManager? _subagentStopHookManager;
    private readonly IAutoRebaseService? _autoRebaseService;
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造 SubagentStopHook 执行器实例
    /// </summary>
    /// <param name="subAgentContextAccessor">子代理上下文访问器，缺省时使用默认实现</param>
    /// <param name="subagentStopHookManager">可选的子代理停止 Hook 管理器</param>
    /// <param name="autoRebaseService">可选的自动 rebase 服务</param>
    /// <param name="logger">可选日志记录器</param>
    public SubagentStopHookRunner(
        ISubAgentContextAccessor? subAgentContextAccessor,
        ISubagentStopHookManager? subagentStopHookManager,
        IAutoRebaseService? autoRebaseService,
        ILogger? logger)
    {
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _subagentStopHookManager = subagentStopHookManager;
        _autoRebaseService = autoRebaseService;
        _logger = logger;
    }

    /// <summary>
    /// 执行 SubagentStop Hook，随后尝试自动 rebase 同步主干
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task OnSubagentStopHookAsync(string agentId, CancellationToken cancellationToken)
    {
        if (_subagentStopHookManager is not null)
        {
            var subAgentContext = _subAgentContextAccessor.Current;
            var agentType = subAgentContext?.Role.ToValue() ?? "executor";
            var sessionId = subAgentContext?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;

            var context = new SubagentStopHookContext
            {
                SessionId = sessionId,
                AgentId = agentId,
                AgentType = agentType,
                WorktreePath = subAgentContext?.WorktreePath,
            };

            var result = await _subagentStopHookManager.OnSubagentStopAsync(context, cancellationToken).ConfigureAwait(false);
            if (!result.ShouldProceed)
            {
                _logger?.LogWarning("[AgentCoordinator] SubagentStop Hook 阻塞了 Agent {AgentId} 的释放: {Message}",
                    agentId, result.Message);
            }
        }

        await TryAutoRebaseAsync(agentId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SubagentStop 时自动 rebase 同步主干 — 取代软通知模式（ADR T5.1）
    /// </summary>
    private async Task TryAutoRebaseAsync(string agentId, CancellationToken cancellationToken)
    {
        if (_autoRebaseService is null)
            return;

        var worktreePath = _subAgentContextAccessor.Current?.WorktreePath;
        if (string.IsNullOrWhiteSpace(worktreePath))
            return;

        try
        {
            var request = new AutoRebaseRequest
            {
                WorktreePath = worktreePath,
                AgentId = agentId,
            };
            var result = await _autoRebaseService.RebaseSyncAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.HadConflicts)
                _logger?.LogWarning("[AgentCoordinator] AutoRebase 冲突 for {AgentId}: {Files}", agentId, string.Join(", ", result.ConflictFiles));
            else if (!result.Success)
                _logger?.LogWarning("[AgentCoordinator] AutoRebase 失败 for {AgentId}: {Message}", agentId, result.Message);
            else if (result.WasSkipped)
                _logger?.LogDebug("[AgentCoordinator] AutoRebase 跳过(无新提交) for {AgentId}", agentId);
            else
                _logger?.LogInformation("[AgentCoordinator] AutoRebase 成功 for {AgentId}", agentId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[AgentCoordinator] AutoRebase 异常 for {AgentId}", agentId);
        }
    }
}
