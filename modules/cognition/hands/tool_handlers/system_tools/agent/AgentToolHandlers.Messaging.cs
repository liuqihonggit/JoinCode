namespace Tools.Handlers;

/// <summary>
/// AgentToolHandlers 消息通信职责 — 发送消息、广播、转发用户输入。
/// 从 AgentToolHandlers.cs 拆分而来（单一职责：Messaging）。
/// </summary>
public sealed partial class AgentToolHandlers {

    /// <summary>
    /// 向运行中的代理发送消息 — 对齐 TS SendMessageTool
    /// 支持: 按名称/ID发送、广播(to="*")、结构化消息(shutdown_request/plan_approval_response)
    /// </summary>
    [McpTool(AgentToolNameEnumConstants.AgentSendMessage, "Send a message to another agent", AgentToolNameEnumConstants.Agent)]
    public async Task<ToolResult> SendMessageAsync(
        [McpToolParameter("Recipient: teammate name, agent ID, or '*' for broadcast")] string to,
        [McpToolParameter("Message content (plain text or structured JSON)")] string message,
        [McpToolParameter("5-10 word summary preview (required for plain text messages)", Required = false)] string? summary = null,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(to)) {
            var recipientDiag = AgentDiagnostics.BuildRecipientEmptyDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(recipientDiag.FormattedMessage)
                .WithDiagnostic(recipientDiag)
                .Build();
        }

        if (string.IsNullOrWhiteSpace(message)) {
            var msgDiag = AgentDiagnostics.BuildMessageEmptyDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(msgDiag.FormattedMessage)
                .WithDiagnostic(msgDiag)
                .Build();
        }

        try {
            // 解析结构化消息 — 对齐 TS SendMessageTool validateInput
            var isStructured = StructuredMessageParser.TryParse(message, out var structuredData);

            // 结构化消息验证 — 对齐 TS validateInput 规则
            if (isStructured && structuredData is not null) {
                // 结构化消息不能广播
                if (to == "*") {
                    var broadcastDiag = AgentDiagnostics.BuildBroadcastStructuredMessageDiagnostic(structuredData.Type.ToValue());
                    return ToolResultBuilder.Error()
                        .WithText(broadcastDiag.FormattedMessage)
                        .WithDiagnostic(broadcastDiag)
                        .Build();
                }

                // shutdown_response 必须发给 team-lead
                if (structuredData.Type == TeammateMessageType.ShutdownApproved ||
                    structuredData.Type == TeammateMessageType.ShutdownRejected) {
                    // 对齐 TS: shutdown_response 必须发给 TEAM_LEAD_NAME
                    // 此处仅记录日志，不强制阻止（C# 端 team-lead 名称可能不同）
                    _logger?.LogDebug("Shutdown response sent to {Recipient}", to);
                }
            } else {
                // 纯文本消息验证 — 对齐 TS: summary 推荐但不强制
                // TS 中 summary 是 "required for plain text messages"，C# 端作为可选参数
                if (string.IsNullOrEmpty(summary)) {
                    _logger?.LogWarning("Agent message sent without summary");
                }
            }

            // 广播模式: to="*"（仅纯文本消息可到达此处）
            if (to == "*") {
                var broadcastResult = await HandleBroadcastAsync(message, summary, cancellationToken).ConfigureAwait(false);
                return broadcastResult;
            }

            // 点对点消息 — 传入结构化消息数据
            var sent = isStructured && structuredData is not null
                ? await _agentService.SendStructuredMessageAsync(to, structuredData, message, cancellationToken).ConfigureAwait(false)
                : await _agentService.SendMessageToAgentAsync(to, message, cancellationToken).ConfigureAwait(false);

            if (!sent) {
                var dryState = TryLoadDryRunState(to);
                if (dryState is not null) {
                    AppendDryRunMessage(to, message, summary);
                    var dryMsg = summary is not null
                        ? $"Message sent to {to}: {summary}"
                        : $"Message sent to {to}";
                    return ToolResultBuilder.Success().WithText(dryMsg).Build();
                }

                ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "send_message", false);
                var sendDiag = AgentDiagnostics.BuildSendMessageFailedDiagnostic(to);
                return ToolResultBuilder.Error()
                    .WithText(sendDiag.FormattedMessage)
                    .WithDiagnostic(sendDiag)
                    .Build();
            }

            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "send_message", true);

            // 结构化消息的响应格式 — 对齐 TS RequestOutput/ResponseOutput
            if (isStructured && structuredData is not null) {
                var typeStr = structuredData.Type.ToValue();
                return structuredData.Type switch {
                    TeammateMessageType.ShutdownRequest => ToolResultBuilder.Success()
                        .WithText($"Shutdown request sent to {to} (request_id: {structuredData.RequestId})")
                        .Build(),
                    TeammateMessageType.ShutdownApproved => ToolResultBuilder.Success()
                        .WithText($"Shutdown approval sent to {to} (request_id: {structuredData.RequestId})")
                        .Build(),
                    TeammateMessageType.ShutdownRejected => ToolResultBuilder.Success()
                        .WithText($"Shutdown rejection sent to {to} (request_id: {structuredData.RequestId})")
                        .Build(),
                    TeammateMessageType.PlanApprovalRequest => ToolResultBuilder.Success()
                        .WithText($"Plan approval request sent to {to}")
                        .Build(),
                    TeammateMessageType.PlanApprovalResponse when structuredData.Approve == true => ToolResultBuilder.Success()
                        .WithText($"Plan approved for {to} (request_id: {structuredData.RequestId})")
                        .Build(),
                    TeammateMessageType.PlanApprovalResponse => ToolResultBuilder.Success()
                        .WithText($"Plan rejected for {to} (request_id: {structuredData.RequestId}){(!string.IsNullOrEmpty(structuredData.Feedback) ? $": {structuredData.Feedback}" : "")}")
                        .Build(),
                    _ => ToolResultBuilder.Success()
                        .WithText($"Structured message ({typeStr}) sent to {to}")
                        .Build()
                };
            }

            var responseText = summary is not null
                ? $"Message sent to {to}: {summary}"
                : $"Message sent to {to}";
            return ToolResultBuilder.Success()
                .WithText(responseText)
                .Build();
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger?.LogError(ex, L.T(StringKey.AgentSendMessageFailed));
            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "send_message", false);
            return ToolExceptionDiagnosticHelper.BuildErrorResult("agent_send_message", ex, _logger, "to", to);
        }
    }

    /// <summary>
    /// 处理广播消息 — 对齐 TS SendMessageTool handleBroadcast
    /// </summary>
    private async Task<ToolResult> HandleBroadcastAsync(string message, string? summary, CancellationToken cancellationToken) {
        // 通过 TeamManager 广播
        var teamManager = _teamManager ?? _serviceProvider?.GetService(typeof(ITeamManager)) as ITeamManager;
        if (teamManager is null) {
            var svcDiag = AgentDiagnostics.BuildBroadcastServiceUnavailableDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(svcDiag.FormattedMessage)
                .WithDiagnostic(svcDiag)
                .Build();
        }

        var teams = await teamManager.ListTeamsAsync(cancellationToken).ConfigureAwait(false);
        if (teams.Count == 0) {
            var noTeamsDiag = AgentDiagnostics.BuildBroadcastNoTeamsDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(noTeamsDiag.FormattedMessage)
                .WithDiagnostic(noTeamsDiag)
                .Build();
        }

        var currentAgentId = _subAgentContextAccessor.Current?.AgentId ?? "unknown";
        var sentCount = 0;

        foreach (var team in teams) {
            var result = await teamManager.BroadcastMessageAsync(
                team.TeamId, currentAgentId, message, null, cancellationToken).ConfigureAwait(false);
            if (result.Success) sentCount++;
        }

        ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "broadcast", sentCount > 0);
        return ToolResultBuilder.Success()
            .WithText($"Broadcast sent to {sentCount} team(s){(summary is not null ? $": {summary}" : "")}")
            .Build();
    }

    /// <summary>
    /// 将用户输入转发给运行中的子代理 — 主代理 LLM 协同决策时调用
    /// 用户在子代理运行期间追加的输入，通过 IAgentInputForwardQueue 入队，子代理每轮 LLM 调用前主动消费
    /// </summary>
    [McpTool(AgentToolNameEnumConstants.ForwardUserInput, "Forward user input to a running sub-agent", AgentToolNameEnumConstants.Agent)]
    public async Task<ToolResult> ForwardUserInputAsync(
        [McpToolParameter("Target sub-agent ID")] string agentId,
        [McpToolParameter("User input to forward")] string userInput,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(agentId)) {
            return ToolResultBuilder.Error()
                .WithText("agentId 不能为空")
                .Build();
        }

        if (string.IsNullOrWhiteSpace(userInput)) {
            return ToolResultBuilder.Error()
                .WithText("userInput 不能为空")
                .Build();
        }

        try {
            var dryState = TryLoadDryRunState(agentId);
            if (dryState is not null) {
                AppendDryRunMessage(agentId, userInput, "forwarded_user_input");
                return ToolResultBuilder.Success()
                    .WithText($"用户输入已转发给子代理 {agentId}")
                    .Build();
            }

            var sent = await _agentService.ForwardUserInputToAgentAsync(agentId, userInput, cancellationToken).ConfigureAwait(false);

            if (!sent) {
                ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "forward_user_input", false);
                return ToolResultBuilder.Error()
                    .WithText($"转发失败：子代理 {agentId} 不存在或转发队列未注册")
                    .Build();
            }

            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "forward_user_input", true);
            return ToolResultBuilder.Success()
                .WithText($"用户输入已转发给子代理 {agentId}")
                .Build();
        } catch (Exception ex) {
            _logger?.LogError(ex, "ForwardUserInput failed for agent {AgentId}", agentId);
            return ToolResultBuilder.Error()
                .WithText($"转发异常: {ex.Message}")
                .Build();
        }
    }
}
