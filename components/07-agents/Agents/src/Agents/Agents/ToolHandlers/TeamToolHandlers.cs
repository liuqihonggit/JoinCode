

namespace Core.Agents.ToolHandlers;

/// <summary>
/// 团队工具处理器 - 提供团队管理功能
/// </summary>
[McpToolHandler(ToolCategory.Team)]
public class TeamToolHandlers
{
    private readonly ITeamManager _teamManager;
    private readonly ITelemetryService? _telemetryService;

    public TeamToolHandlers(ITeamManager teamManager, ITelemetryService? telemetryService = null)
    {
        _teamManager = teamManager ?? throw new ArgumentNullException(nameof(teamManager));
        _telemetryService = telemetryService;
    }

    /// <summary>
    /// 创建团队
    /// </summary>
    [McpTool(TeamToolNameConstants.TeamCreate, "Create a new team", "team")]
    public async Task<ToolResult> TeamCreateAsync(
        [McpToolParameter("Team name")] string team_name,
        [McpToolParameter("Team description (optional)", Required = false)] string? description = null,
        [McpToolParameter("Initial member list (optional)", Required = false)] List<string>? initial_members = null,
        CancellationToken cancellationToken = default)
    {
        var command = new TeamCreateCommand(team_name, description, initial_members);
        var validationError = ValidateCommand(command);
        if (validationError != null)
        {
            return McpResultBuilder.Error().WithText(validationError).Build();
        }

        var result = await _teamManager.CreateTeamAsync(
            command.TeamName,
            command.Description,
            command.InitialMembers,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordTeamMetrics("create", "failed");
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? L.T(StringKey.TeamCreateFailed)).Build();
        }

        var response = FormatTeamResponse(result.Data ?? throw new InvalidOperationException("Team creation succeeded but no data was returned."), L.T(StringKey.TeamCreated));
        RecordTeamMetrics("create", "ok");
        return McpResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 删除团队
    /// </summary>
    [McpTool(TeamToolNameConstants.TeamDelete, "Delete a team", "team")]
    public async Task<ToolResult> TeamDeleteAsync(
        [McpToolParameter("Team ID")] string team_id,
        CancellationToken cancellationToken = default)
    {
        var command = new TeamDeleteCommand(team_id);
        var validationError = ValidateCommand(command);
        if (validationError != null)
        {
            return McpResultBuilder.Error().WithText(validationError).Build();
        }

        var result = await _teamManager.DeleteTeamAsync(command.TeamId, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? L.T(StringKey.TeamDeleteFailed)).Build();
        }

        var response = L.T(StringKey.TeamDeleted, command.TeamId);
        return McpResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 获取团队信息
    /// </summary>
    [McpTool(TeamToolNameConstants.TeamGet, "Get team information", "team")]
    public async Task<ToolResult> TeamGetAsync(
        [McpToolParameter("Team ID")] string team_id,
        CancellationToken cancellationToken = default)
    {
        var command = new TeamGetCommand(team_id);
        var validationError = ValidateCommand(command);
        if (validationError != null)
        {
            return McpResultBuilder.Error().WithText(validationError).Build();
        }

        var team = await _teamManager.GetTeamAsync(command.TeamId, cancellationToken).ConfigureAwait(false);

        if (team == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.TeamNotFound, command.TeamId)).Build();
        }

        var response = FormatTeamResponse(team, L.T(StringKey.TeamInfo));
        return McpResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 列出所有团队
    /// </summary>
    [McpTool(TeamToolNameConstants.TeamList, "List all teams", "team")]
    public async Task<ToolResult> TeamListAsync(
        CancellationToken cancellationToken = default)
    {
        var teams = await _teamManager.ListTeamsAsync(cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.TeamListCount, teams.Count));
        response.AppendLine();

        if (teams.Count == 0)
        {
            response.AppendLine(L.T(StringKey.NoTeams));
        }
        else
        {
            foreach (var team in teams)
            {
                response.AppendLine(FormatTeamSummary(team));
            }
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 添加团队成员
    /// </summary>
    [McpTool(TeamToolNameConstants.TeamAddMember, "Add team member", "team")]
    public async Task<ToolResult> TeamAddMemberAsync(
        [McpToolParameter("Team ID")] string team_id,
        [McpToolParameter("Agent ID")] string agent_id,
        CancellationToken cancellationToken = default)
    {
        var command = new TeamAddMemberCommand(team_id, agent_id);
        var validationError = ValidateCommand(command);
        if (validationError != null)
        {
            return McpResultBuilder.Error().WithText(validationError).Build();
        }

        var result = await _teamManager.AddTeamMemberAsync(
            command.TeamId,
            command.AgentId,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? L.T(StringKey.AddMemberFailed)).Build();
        }

        var response = L.T(StringKey.MemberAdded, command.TeamId, command.AgentId);
        return McpResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 移除团队成员
    /// </summary>
    [McpTool(TeamToolNameConstants.TeamRemoveMember, "Remove team member", "team")]
    public async Task<ToolResult> TeamRemoveMemberAsync(
        [McpToolParameter("Team ID")] string team_id,
        [McpToolParameter("Agent ID")] string agent_id,
        CancellationToken cancellationToken = default)
    {
        var command = new TeamRemoveMemberCommand(team_id, agent_id);
        var validationError = ValidateCommand(command);
        if (validationError != null)
        {
            return McpResultBuilder.Error().WithText(validationError).Build();
        }

        var result = await _teamManager.RemoveTeamMemberAsync(
            command.TeamId,
            command.AgentId,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? L.T(StringKey.RemoveMemberFailed)).Build();
        }

        var response = L.T(StringKey.MemberRemoved, command.TeamId, command.AgentId);
        return McpResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 发送团队消息
    /// </summary>
    [McpTool(TeamToolNameConstants.TeamSendMessage, "Send team message", "team")]
    public async Task<ToolResult> TeamSendMessageAsync(
        [McpToolParameter("Team ID")] string team_id,
        [McpToolParameter("Sender ID")] string sender_id,
        [McpToolParameter("Message content")] string content,
        [McpToolParameter("Message type (optional)", Required = false)] string? message_type = null,
        CancellationToken cancellationToken = default)
    {
        var command = new TeamSendMessageCommand(team_id, sender_id, content, message_type);
        var validationError = ValidateCommand(command);
        if (validationError != null)
        {
            return McpResultBuilder.Error().WithText(validationError).Build();
        }

        var result = await _teamManager.SendMessageAsync(
            command.TeamId,
            command.SenderId,
            command.Content,
            command.MessageType,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordTeamMetrics("send_message", "failed");
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? L.T(StringKey.SendMessageFailed)).Build();
        }

        var response = L.T(StringKey.MessageSentToTeam, command.TeamId, command.SenderId);
        RecordTeamMetrics("send_message", "ok");
        return McpResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 发送私信
    /// </summary>
    [McpTool(TeamToolNameConstants.TeamSendDirectMessage, "Send direct message to specified agent", "team")]
    public async Task<ToolResult> TeamSendDirectMessageAsync(
        [McpToolParameter("Target agent ID")] string target_agent_id,
        [McpToolParameter("Sender ID")] string sender_id,
        [McpToolParameter("Message content")] string content,
        [McpToolParameter("Message type (optional)", Required = false)] string? message_type = null,
        CancellationToken cancellationToken = default)
    {
        var command = new TeamSendDirectMessageCommand(target_agent_id, sender_id, content, message_type);
        var validationError = ValidateCommand(command);
        if (validationError != null)
        {
            return McpResultBuilder.Error().WithText(validationError).Build();
        }

        var result = await _teamManager.SendMessageToAgentAsync(
            command.TargetAgentId,
            command.SenderId,
            command.Content,
            command.MessageType,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? L.T(StringKey.DirectMessageFailed)).Build();
        }

        var response = L.T(StringKey.DirectMessageSent, command.TargetAgentId, command.SenderId);
        return McpResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 广播消息
    /// </summary>
    [McpTool(TeamToolNameConstants.TeamBroadcast, "Broadcast message to team members", "team")]
    public async Task<ToolResult> TeamBroadcastAsync(
        [McpToolParameter("Team ID")] string team_id,
        [McpToolParameter("Sender ID")] string sender_id,
        [McpToolParameter("Message content")] string content,
        [McpToolParameter("Message type (optional)", Required = false)] string? message_type = null,
        CancellationToken cancellationToken = default)
    {
        var command = new TeamBroadcastMessageCommand(team_id, sender_id, content, message_type);
        var validationError = ValidateCommand(command);
        if (validationError != null)
        {
            return McpResultBuilder.Error().WithText(validationError).Build();
        }

        var result = await _teamManager.BroadcastMessageAsync(
            command.TeamId,
            command.SenderId,
            command.Content,
            command.MessageType,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? L.T(StringKey.BroadcastFailed)).Build();
        }

        var response = L.T(StringKey.BroadcastSent, command.TeamId, command.SenderId);
        return McpResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 获取团队消息
    /// </summary>
    [McpTool(TeamToolNameConstants.TeamGetMessages, "Get team message history", "team")]
    public async Task<ToolResult> TeamGetMessagesAsync(
        [McpToolParameter("Team ID")] string team_id,
        [McpToolParameter("Message count limit (optional, default 50)", Required = false)] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var command = new TeamGetMessagesCommand(team_id, limit);
        var validationError = ValidateCommand(command);
        if (validationError != null)
        {
            return McpResultBuilder.Error().WithText(validationError).Build();
        }

        var messages = await _teamManager.GetTeamMessagesAsync(
            command.TeamId,
            command.Limit ?? 50,
            cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.TeamMessageHistory, command.TeamId));
        response.AppendLine(L.T(StringKey.MessageCount, messages.Count));
        response.AppendLine();

        if (messages.Count == 0)
        {
            response.AppendLine(L.T(StringKey.NoMessages));
        }
        else
        {
            foreach (var message in messages.OrderBy(m => m.Timestamp))
            {
                response.AppendLine($"[{message.Timestamp:HH:mm:ss}] {message.SenderId}: {message.Content}");
            }
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    #region Private Methods

    private void RecordTeamMetrics(string operation, string result)
        => _telemetryService?.RecordCount("team.handler.count", new Dictionary<string, string> { ["operation"] = operation, ["result"] = result }, "count", "Team handler count");

    private static string? ValidateCommand<TCommand>(TCommand command)
    {
        return command switch
        {
            TeamCreateCommand cmd => string.IsNullOrWhiteSpace(cmd.TeamName) ? L.T(StringKey.TeamNameCannotBeEmpty) : null,
            TeamDeleteCommand cmd => string.IsNullOrWhiteSpace(cmd.TeamId) ? L.T(StringKey.TeamIdCannotBeEmpty) : null,
            TeamGetCommand cmd => string.IsNullOrWhiteSpace(cmd.TeamId) ? L.T(StringKey.TeamIdCannotBeEmpty) : null,
            TeamAddMemberCommand cmd => string.IsNullOrWhiteSpace(cmd.TeamId) ? L.T(StringKey.TeamIdCannotBeEmpty)
                : string.IsNullOrWhiteSpace(cmd.AgentId) ? L.T(StringKey.AgentIdCannotBeEmpty) : null,
            TeamRemoveMemberCommand cmd => string.IsNullOrWhiteSpace(cmd.TeamId) ? L.T(StringKey.TeamIdCannotBeEmpty)
                : string.IsNullOrWhiteSpace(cmd.AgentId) ? L.T(StringKey.AgentIdCannotBeEmpty) : null,
            TeamSendMessageCommand cmd => string.IsNullOrWhiteSpace(cmd.TeamId) ? L.T(StringKey.TeamIdCannotBeEmpty)
                : string.IsNullOrWhiteSpace(cmd.SenderId) ? L.T(StringKey.SenderIdCannotBeEmpty)
                : string.IsNullOrWhiteSpace(cmd.Content) ? L.T(StringKey.ContentCannotBeEmpty) : null,
            TeamSendDirectMessageCommand cmd => string.IsNullOrWhiteSpace(cmd.TargetAgentId) ? L.T(StringKey.TargetAgentIdCannotBeEmpty)
                : string.IsNullOrWhiteSpace(cmd.SenderId) ? L.T(StringKey.SenderIdCannotBeEmpty)
                : string.IsNullOrWhiteSpace(cmd.Content) ? L.T(StringKey.ContentCannotBeEmpty) : null,
            TeamBroadcastMessageCommand cmd => string.IsNullOrWhiteSpace(cmd.TeamId) ? L.T(StringKey.TeamIdCannotBeEmpty)
                : string.IsNullOrWhiteSpace(cmd.SenderId) ? L.T(StringKey.SenderIdCannotBeEmpty)
                : string.IsNullOrWhiteSpace(cmd.Content) ? L.T(StringKey.ContentCannotBeEmpty) : null,
            TeamGetMessagesCommand cmd => string.IsNullOrWhiteSpace(cmd.TeamId) ? L.T(StringKey.TeamIdCannotBeEmpty) : null,
            _ => null
        };
    }

    private static string FormatTeamResponse(TeamInfo team, string header)
    {
        var response = new System.Text.StringBuilder();
        response.AppendLine($"{header}");
        response.AppendLine($"ID: {team.TeamId}");
        response.AppendLine(L.T(StringKey.LabelTeamName, team.TeamName));

        if (!string.IsNullOrEmpty(team.Description))
        {
            response.AppendLine(L.T(StringKey.LabelTeamDescription, team.Description));
        }

        response.AppendLine(L.T(StringKey.LabelMemberCount, team.Members.Count));
        if (team.Members.Count > 0)
        {
            response.AppendLine(L.T(StringKey.LabelMembers, string.Join(", ", team.Members)));
        }

        response.AppendLine(L.T(StringKey.TeamLabelCreatedTime, team.CreatedAt.ToString("yyyy-MM-dd HH:mm")));
        response.AppendLine(L.T(StringKey.LabelLastActivity, team.LastActivityAt.ToString("yyyy-MM-dd HH:mm")));

        return response.ToString();
    }

    private static string FormatTeamSummary(TeamInfo team)
    {
        var memberCount = team.Members.Count;
        return L.T(StringKey.TeamSummaryFormat, team.TeamId, team.TeamName, memberCount, team.LastActivityAt.ToString("MM-dd HH:mm"));
    }

    #endregion
}
