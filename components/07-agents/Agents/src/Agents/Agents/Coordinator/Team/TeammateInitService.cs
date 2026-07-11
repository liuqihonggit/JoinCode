
namespace Core.Agents.Coordinator;

[Register(typeof(ITeammateInitService))]
public sealed partial class TeammateInitService : ITeammateInitService
{
    private readonly ITeamManager _teamManager;
    private readonly ISessionHookManager? _sessionHookManager;
    private readonly IAgentMessageBroker? _messageBroker;
    private readonly ILogger? _logger;
    [Inject] private readonly IClockService _clock;

    public TeammateInitService(
        ITeamManager teamManager,
        ISessionHookManager? sessionHookManager = null,
        IAgentMessageBroker? messageBroker = null,
        ILogger? logger = null,
        IClockService? clock = null)
    {
        _teamManager = teamManager ?? throw new ArgumentNullException(nameof(teamManager));
        _sessionHookManager = sessionHookManager;
        _messageBroker = messageBroker;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    public async Task<TeammateInitContext?> BuildInitContextAsync(string teamId, string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var team = await _teamManager.GetTeamAsync(teamId, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            _logger?.LogWarning("[TeammateInitService] 团队不存在: {TeamId}", teamId);
            return null;
        }

        var members = await _teamManager.GetTeamMembersAsync(teamId, cancellationToken).ConfigureAwait(false);
        var otherMembers = members.Where(m => m != agentId).ToList();
        var allowedPaths = await _teamManager.GetTeamAllowedPathsAsync(teamId, cancellationToken).ConfigureAwait(false);

        return new TeammateInitContext
        {
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

    public async Task InitializeTeammateHooksAsync(string teamId, string agentId, string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (_sessionHookManager is null)
        {
            _logger?.LogDebug("[TeammateInitService] ISessionHookManager 未注册，跳过钩子初始化");
            return;
        }

        var hookId = await _sessionHookManager.AddFunctionHookAsync(
            sessionId,
            HookEvent.Stop,
            matcher: null,
            callback: async (input, ct) =>
            {
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
        string teamId, string agentId, HookInput input, CancellationToken ct)
    {
        if (_messageBroker is null)
        {
            return HookResult.Success();
        }

        try
        {
            var team = await _teamManager.GetTeamAsync(teamId, ct).ConfigureAwait(false);
            var teamName = team?.TeamName ?? teamId;

            var idleNotification = new TeammateIdleNotification
            {
                AgentId = agentId,
                TeamName = teamName,
                TeamId = teamId,
                Timestamp = _clock.GetUtcNow()
            };

            var serialized = System.Text.Json.JsonSerializer.Serialize(
                idleNotification,
                TeammateInitJsonContext.Default.TeammateIdleNotification);

            var message = new CoordinatorAgentMessage
            {
                FromAgentId = agentId,
                ToAgentId = "coordinator",
                MessageType = JoinCode.Abstractions.Models.Agent.TeammateMessageType.IdleNotification.ToString(),
                Content = serialized
            };

            await _messageBroker.SendMessageAsync(agentId, message, ct).ConfigureAwait(false);

            _logger?.LogDebug("[TeammateInitService] Teammate {AgentId} Stop Hook 触发空闲通知", agentId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "[TeammateInitService] Teammate {AgentId} Stop Hook 执行异常", agentId);
        }

        return HookResult.Success();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(TeammateIdleNotification))]
internal sealed partial class TeammateInitJsonContext : JsonSerializerContext;
