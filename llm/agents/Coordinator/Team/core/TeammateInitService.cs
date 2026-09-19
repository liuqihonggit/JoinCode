
namespace Core.Agents.Coordinator;

/// <summary>队友初始化服务 — 负责新队友的创建、会话挂钩注册与初始消息分发</summary>
[Register(typeof(ITeammateInitService), ServiceLifetime.Singleton)]
public sealed partial class TeammateInitService : ServiceEntity, ITeammateInitService {
    private readonly ITeamManager _teamManager;
    private readonly ISessionHookManager? _sessionHookManager;
    private readonly IMailbox? _messageBroker;
    private readonly ILogger? _logger;
    private readonly IClockService _clock;

    /// <summary>
    /// 初始化 Teammate 初始化服务
    /// </summary>
    /// <param name="teamManager">团队管理器</param>
    /// <param name="sessionHookManager">会话钩子管理器</param>
    /// <param name="messageBroker">消息邮箱</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="clock">时钟服务</param>
    public TeammateInitService(
        ITeamManager teamManager,
        ISessionHookManager? sessionHookManager = null,
        IMailbox? messageBroker = null,
        ILogger? logger = null,
        IClockService? clock = null) {
        _teamManager = teamManager ?? throw new ArgumentNullException(nameof(teamManager));
        _sessionHookManager = sessionHookManager;
        _messageBroker = messageBroker;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 异步构建 Teammate 初始化上下文，包含团队成员、协调器与允许路径等信息
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="agentId">智能体标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>初始化上下文，团队不存在则返回 null</returns>
    public async Task<TeammateInitContext?> BuildInitContextAsync(string teamId, string agentId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var team = await _teamManager.GetTeamAsync(teamId, cancellationToken).ConfigureAwait(false);
        if (team is null) {
            _logger?.LogWarning("[TeammateInitService] 团队不存在: {TeamId}", teamId);
            return null;
        }

        var members = await _teamManager.GetTeamMembersAsync(teamId, cancellationToken).ConfigureAwait(false);
        var otherMembers = members.Where(m => m != agentId).ToList();
        var allowedPaths = await _teamManager.GetTeamAllowedPathsAsync(teamId, cancellationToken).ConfigureAwait(false);

        return new TeammateInitContext {
            TeamId = teamId,
            TeamName = team.TeamName,
            AgentId = agentId,
            TeamDescription = team.Description,
            OtherMembers = otherMembers,
            CoordinatorId = otherMembers.FirstOrDefault(),
            LeadAgentId = team.LeadAgentId,
            AllowedPaths = allowedPaths
        };
    }

    /// <summary>
    /// 异步为 Teammate 注册 Stop 钩子，在会话停止时触发空闲通知
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="agentId">智能体标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InitializeTeammateHooksAsync(string teamId, string agentId, string sessionId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (_sessionHookManager is null) {
            _logger?.LogDebug("[TeammateInitService] ISessionHookManager 未注册，跳过钩子初始化");
            return;
        }

        var hookId = await _sessionHookManager.AddFunctionHookAsync(
            sessionId,
            HookEvent.Stop,
            matcher: null,
            callback: async (input, ct) => {
                return await HandleStopHookAsync(teamId, agentId, input, ct).ConfigureAwait(false);
            },
            errorMessage: "Teammate Stop Hook 执行失败",
            timeout: 5000,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation(
            "[TeammateInitService] 已为 Teammate {AgentId} 注册 Stop Hook (HookId={HookId}, SessionId={SessionId})",
            agentId, hookId, sessionId);
    }

    private async Task<HookResult> HandleStopHookAsync(
        string teamId, string agentId, HookInput input, CancellationToken ct) {
        if (_messageBroker is null) {
            return HookResult.Success();
        }

        try {
            var team = await _teamManager.GetTeamAsync(teamId, ct).ConfigureAwait(false);
            var teamName = team?.TeamName ?? teamId;

            var idleNotification = new TeammateIdleNotification {
                AgentId = agentId,
                TeamName = teamName,
                TeamId = teamId,
                Timestamp = _clock.GetUtcNow()
            };

            var serialized = System.Text.Json.JsonSerializer.Serialize(
                idleNotification,
                TeammateInitJsonContext.Default.TeammateIdleNotification);

            var message = new CoordinatorAgentMessage {
                FromAgentId = agentId,
                ToAgentId = "coordinator",
                MessageType = JoinCode.Abstractions.Models.Agent.TeammateMessageType.IdleNotification.ToString(),
                Content = serialized
            };

            await _messageBroker.SendAsync(agentId, message, ct).ConfigureAwait(false);

            _logger?.LogDebug("[TeammateInitService] Teammate {AgentId} Stop Hook 触发空闲通知", agentId);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogWarning(ex, "[TeammateInitService] Teammate {AgentId} Stop Hook 执行异常", agentId);
        }

        return HookResult.Success();
    }
}