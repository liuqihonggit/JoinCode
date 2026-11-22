namespace Core.Agents.Coordinator.Liveness;

/// <summary>
/// 子代理激活结果
/// </summary>
public sealed record ActivationResult {
    /// <summary>子代理 ID</summary>
    public required string AgentId { get; init; }
    /// <summary>是否成功激活</summary>
    public required bool Success { get; init; }
    /// <summary>原因描述</summary>
    public string? Reason { get; init; }

    /// <summary>激活成功</summary>
    public static ActivationResult Succeeded(string agentId) => new() { AgentId = agentId, Success = true };
    /// <summary>Agent 不存在</summary>
    public static ActivationResult AgentNotFound(string agentId) => new() { AgentId = agentId, Success = false, Reason = "Agent 不存在" };
    /// <summary>Agent 已在运行</summary>
    public static ActivationResult AlreadyRunning(string agentId) => new() { AgentId = agentId, Success = true, Reason = "Agent 已在运行" };
}

/// <summary>
/// 子代理激活器 — L3 干预层（ADR 0106）
/// <para>
/// 检测到子代理疑似卡死后执行激活：
/// 1. 注入催促提示消息到 ChatHistory（不改变前缀，保 KV cache）
/// 2. 调用 ResumeAgentAsync 恢复执行
/// 3. Touch() 刷新 LastActivityAt
/// </para>
/// </summary>
public sealed class SubAgentActivator {
    private readonly IAgentLifecycleManager _lifecycleManager;
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造子代理激活器
    /// </summary>
    public SubAgentActivator(IAgentLifecycleManager lifecycleManager, ILogger? logger = null) {
        _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
        _logger = logger;
    }

    /// <summary>
    /// 激活疑似卡死的子代理 — 注入催促提示 + 恢复执行
    /// </summary>
    /// <param name="agentId">卡死的子代理 ID</param>
    /// <param name="idleSeconds">无活动时长（秒），用于提示消息</param>
    /// <param name="ct">取消令牌</param>
    public async Task<ActivationResult> ActivateAsync(string agentId, int idleSeconds = 30, CancellationToken ct = default) {
        _logger?.LogDebug("[SubAgentActivator] 尝试激活 Agent {AgentId}，无活动 {Seconds}s", agentId, idleSeconds);

        var agent = await _lifecycleManager.GetAgentAsync(agentId, ct).ConfigureAwait(false);
        if (agent is null) {
            _logger?.LogWarning("[SubAgentActivator] Agent {AgentId} 不存在，无法激活", agentId);
            return ActivationResult.AgentNotFound(agentId);
        }

        // 注入催促提示（System 消息，不改变前缀，保 KV cache）
        var prompt = $"[系统提示] 检测到 {idleSeconds}s 无活动输出。请继续执行任务，或报告当前阻塞原因。如果任务已完成，请输出结果。";
        agent.ChatHistory.AddSystemMessage(prompt);

        // 恢复执行（如果处于 Paused 状态）
        if (agent.Status == TaskExecutionStatus.Paused) {
            await _lifecycleManager.ResumeAgentAsync(agentId, ct).ConfigureAwait(false);
            _logger?.LogInformation("[SubAgentActivator] Agent {AgentId} 已从 Paused 恢复", agentId);
        }

        // 刷新活动时间
        if (agent is Entity entity)
            entity.Touch();

        _logger?.LogInformation("[SubAgentActivator] Agent {AgentId} 已激活，注入催促提示", agentId);
        return ActivationResult.Succeeded(agentId);
    }
}