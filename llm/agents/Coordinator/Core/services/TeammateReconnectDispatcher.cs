namespace Core.Agents.Coordinator;

/// <summary>
/// 队友重连调度器 — 包装 ITeammateReconnectService，提供 null 安全与异常吞咽
/// </summary>
internal sealed class TeammateReconnectDispatcher {
    private readonly JoinCode.Abstractions.Interfaces.ITeammateReconnectService? _reconnectService;
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造队友重连调度器实例
    /// </summary>
    /// <param name="reconnectService">可选的队友重连服务，未注册时重连方法返回 null/空</param>
    /// <param name="logger">可选日志记录器</param>
    public TeammateReconnectDispatcher(JoinCode.Abstractions.Interfaces.ITeammateReconnectService? reconnectService, ILogger? logger) {
        _reconnectService = reconnectService;
        _logger = logger;
    }

    /// <summary>
    /// 重连已断开的队友 — 委托给队友重连服务
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="agentId">目标队友标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>重连结果；服务未注册或重连失败时返回 null</returns>
    public async Task<JoinCode.Abstractions.Interfaces.ReconnectResult?> ReconnectDisconnectedTeammateAsync(string teamId, string agentId, CancellationToken cancellationToken = default) {
        if (_reconnectService is null) {
            _logger?.LogWarning("[AgentCoordinator] ITeammateReconnectService 未注册，无法重连");
            return null;
        }

        try {
            return await _reconnectService.ReconnectTeammateAsync(teamId, agentId, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogError(ex, "[AgentCoordinator] 重连 Teammate {AgentId} 失败", agentId);
            return null;
        }
    }

    /// <summary>
    /// 批量重连团队中所有已断开的队友 — 委托给队友重连服务
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>重连结果列表；服务未注册时返回空列表</returns>
    public async Task<IReadOnlyList<JoinCode.Abstractions.Interfaces.ReconnectResult>> ReconnectAllDisconnectedAsync(string teamId, CancellationToken cancellationToken = default) {
        if (_reconnectService is null) {
            _logger?.LogWarning("[AgentCoordinator] ITeammateReconnectService 未注册，无法批量重连");
            return [];
        }

        try {
            var result = await _reconnectService.ReconnectAllDisconnectedAsync(teamId, cancellationToken).ConfigureAwait(false);
            return new[] { result };
        } catch (Exception ex) {
            _logger?.LogError(ex, "[AgentCoordinator] 批量重连 Teammate 失败");
            return [];
        }
    }
}