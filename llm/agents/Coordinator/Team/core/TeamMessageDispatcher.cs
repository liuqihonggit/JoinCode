namespace Core.Agents.Coordinator;

/// <summary>
/// 团队消息投递器 — 负责将团队消息持久化到文件邮箱与 MailboxHub 跨通道广播
/// </summary>
internal sealed class TeamMessageDispatcher
{
    private readonly TeamRegistry _registry;
    private readonly ITeammateMailboxService? _mailboxService;
    private readonly MailboxHub? _mailboxHub;
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造团队消息投递器实例
    /// </summary>
    /// <param name="registry">团队注册表</param>
    /// <param name="mailboxService">可选的文件邮箱服务</param>
    /// <param name="mailboxHub">可选的邮箱中枢</param>
    /// <param name="logger">可选日志记录器</param>
    public TeamMessageDispatcher(TeamRegistry registry, ITeammateMailboxService? mailboxService, MailboxHub? mailboxHub, ILogger? logger)
    {
        _registry = registry;
        _mailboxService = mailboxService;
        _mailboxHub = mailboxHub;
        _logger = logger;
    }

    /// <summary>
    /// 按消息可见性过滤投递目标 — ADR 0109 决策8。
    /// <para>Public/System → 所有成员（排除发送者）</para>
    /// <para>AdminOnly → 仅管理员/群主（排除发送者）</para>
    /// <para>Private → 仅 ToAgentId</para>
    /// <para>Hidden → 空列表（仅持久化不投递）</para>
    /// </summary>
    public IReadOnlyList<string> FilterRecipientsByVisibility(string teamId, TeamMessage message)
    {
        if (message.Visibility == MessageVisibility.Hidden) return Array.Empty<string>();

        if (message.Visibility == MessageVisibility.Private)
        {
            return message.ToAgentId is not null ? new[] { message.ToAgentId } : Array.Empty<string>();
        }

        if (!_registry.TryGetRoom(teamId, out var room)) return Array.Empty<string>();
        var members = room.Members;

        if (message.Visibility == MessageVisibility.AdminOnly)
        {
            var details = room.MemberDetails;
            var team = room.Info;
            return members
                .Where(m => m != message.SenderId && IsAdminOrOwner(m, details, team))
                .ToList();
        }

        return members.Where(m => m != message.SenderId).ToList();
    }

    /// <summary>
    /// 判断成员是否为管理员或群主
    /// </summary>
    public static bool IsAdminOrOwner(string agentId, Dictionary<string, TeamMemberInfo>? details, TeamInfo? team)
    {
        if (agentId == team?.LeadAgentId) return true;
        if (details is not null && details.TryGetValue(agentId, out var md))
        {
            return md.Role is "admin" or "owner";
        }
        return false;
    }

    /// <summary>
    /// 持久化团队消息到文件邮箱与 MailboxHub
    /// </summary>
    public async Task PersistTeamMessageToMailboxAsync(string teamId, TeamMessage message, CancellationToken cancellationToken)
    {
        var targetAgentIds = FilterRecipientsByVisibility(teamId, message);
        if (targetAgentIds.Count == 0) return;

        var sessionId = _registry.TryGetRoom(teamId, out var room) ? room.SessionId : null;

        if (_mailboxService is not null && sessionId is not null)
        {
            var tasks = targetAgentIds
                .Select(m => _mailboxService.SendAsync(new MailboxSendRequest
                {
                    FromAgentId = message.SenderId,
                    ToAgentId = m,
                    MessageType = message.MessageType,
                    Content = message.Content,
                    SessionId = sessionId
                }, cancellationToken).AsTask())
                .ToArray();

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "广播团队消息到文件邮箱失败");
            }
        }

        if (_mailboxHub is not null)
        {
            var coordinatorMsg = new CoordinatorMessage
            {
                FromAgentId = message.SenderId,
                ToAgentId = message.Visibility == MessageVisibility.Private ? message.ToAgentId ?? "" : "broadcast",
                MessageType = message.MessageType,
                Content = message.Content,
                Visibility = message.Visibility,
            };

            try
            {
                await _mailboxHub.BroadcastAsync(coordinatorMsg, message.Visibility, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "广播团队消息到 MailboxHub 跨通道失败");
            }
        }
    }

    /// <summary>
    /// 持久化直发消息到文件邮箱与 MailboxHub
    /// </summary>
    public async Task PersistDirectMessageToMailboxAsync(
        string targetAgentId, string senderId, string content, string messageType, string teamId,
        CancellationToken cancellationToken)
    {
        var sessionId = _registry.TryGetRoom(teamId, out var room) ? room.SessionId : null;

        if (_mailboxService is not null && sessionId is not null)
        {
            try
            {
                await _mailboxService.SendAsync(new MailboxSendRequest
                {
                    FromAgentId = senderId,
                    ToAgentId = targetAgentId,
                    MessageType = messageType,
                    Content = content,
                    SessionId = sessionId
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex2) when (ex2 is not OperationCanceledException)
            {
                _logger?.LogWarning(ex2, "持久化直发消息到文件邮箱失败");
            }
        }

        if (_mailboxHub is not null)
        {
            var coordinatorMsg = new CoordinatorMessage
            {
                FromAgentId = senderId,
                ToAgentId = targetAgentId,
                MessageType = messageType,
                Content = content,
            };

            try
            {
                await _mailboxHub.SendAsync(targetAgentId, coordinatorMsg, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex2) when (ex2 is not OperationCanceledException)
            {
                _logger?.LogWarning(ex2, "直发消息到 MailboxHub 失败");
            }
        }
    }
}
