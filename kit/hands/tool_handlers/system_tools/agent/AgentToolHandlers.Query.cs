namespace Tools.Handlers;

/// <summary>
/// AgentToolHandlers 查询职责 — 列出代理类型、获取状态、停止代理、列出运行中代理、获取消息。
/// 从 AgentToolHandlers.cs 拆分而来（单一职责：Query）。
/// </summary>
public sealed partial class AgentToolHandlers {

    /// <summary>
    /// 列出可用的代理类型
    /// </summary>
    [McpTool(AgentToolNameEnumConstants.AgentList, "List available agent types", AgentToolNameEnumConstants.Agent, ConcurrencySafe = true)]
    public async Task<ToolResult> ListAgentTypesAsync(
        CancellationToken cancellationToken = default) {
        var types = await _agentService.GetAvailableAgentTypesAsync(cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine("Available agent types:");
        response.AppendLine();

        if (types.Count == 0) {
            response.AppendLine("No predefined agent types available. You can use the generic agent.");
        } else {
            response.AppendLine(string.Join("\n", types.Select(type => {
                var lines = new List<string> { $"- {type.Name}: {type.Description}" };
                if (type.AvailableTools?.Count > 0) {
                    lines.Add($"  Tools: {string.Join(", ", type.AvailableTools)}");
                }
                return string.Join("\n", lines);
            })));
        }

        return ToolResultBuilder.Success()
            .WithText(response.ToString())
            .Build();
    }

    /// <summary>
    /// 获取代理状态
    /// </summary>
    [McpTool(AgentToolNameEnumConstants.AgentStatus, "Get agent status", AgentToolNameEnumConstants.Agent, ConcurrencySafe = true)]
    public async Task<ToolResult> GetAgentStatusAsync(
        [McpToolParameter("Agent ID or name")] string agent_id,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(agent_id)) {
            var idDiag = AgentDiagnostics.BuildAgentIdEmptyDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(idDiag.FormattedMessage)
                .WithDiagnostic(idDiag)
                .Build();
        }

        var agent = await _agentService.GetAgentAsync(agent_id, cancellationToken).ConfigureAwait(false);

        if (agent == null && TryLoadDryRunState(agent_id) is { } dryState) {
            var dryResponse = new System.Text.StringBuilder();
            dryResponse.AppendLine($"Agent status: {dryState.Status}");
            dryResponse.AppendLine($"Agent ID: {dryState.Id}");
            dryResponse.AppendLine($"Description: {dryState.Description}");
            dryResponse.AppendLine($"Started at: {dryState.StartedAt:yyyy-MM-dd HH:mm:ss}");
            if (dryState.CompletedAt.HasValue)
                dryResponse.AppendLine($"Completed at: {dryState.CompletedAt.Value:yyyy-MM-dd HH:mm:ss}");
            if (!string.IsNullOrEmpty(dryState.Prompt))
                dryResponse.AppendLine($"Prompt: {dryState.Prompt}");
            if (!string.IsNullOrEmpty(dryState.IsolationMode))
                dryResponse.AppendLine($"Isolation: {dryState.IsolationMode}");
            if (!string.IsNullOrEmpty(dryState.WorktreePath))
                dryResponse.AppendLine($"WorktreePath: {dryState.WorktreePath}");
            if (!string.IsNullOrEmpty(dryState.WorktreeBranch))
                dryResponse.AppendLine($"WorktreeBranch: {dryState.WorktreeBranch}");
            return ToolResultBuilder.Success().WithText(dryResponse.ToString()).Build();
        }

        if (agent == null) {
            var notFoundDiag = AgentDiagnostics.BuildAgentNotFoundDiagnostic(agent_id);
            return ToolResultBuilder.Error()
                .WithText(notFoundDiag.FormattedMessage)
                .WithDiagnostic(notFoundDiag)
                .Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"Agent status: {agent.Status}");
        response.AppendLine($"Agent ID: {agent.Id}");
        response.AppendLine($"Description: {agent.Description}");

        if (!string.IsNullOrEmpty(agent.Role.ToValue())) {
            response.AppendLine($"Type: {agent.Variant?.ToValue() ?? agent.Role.ToValue()}");
        }

        if (agent.StartedAt.HasValue) {
            response.AppendLine($"Started at: {agent.StartedAt.Value:yyyy-MM-dd HH:mm:ss}");
        }

        if (agent.CompletedAt.HasValue) {
            response.AppendLine($"Completed at: {agent.CompletedAt.Value:yyyy-MM-dd HH:mm:ss}");
        }

        if (!string.IsNullOrEmpty(agent.Output)) {
            response.AppendLine();
            response.AppendLine("Output:");
            response.AppendLine(agent.Output);
        }

        return ToolResultBuilder.Success()
            .WithText(response.ToString())
            .Build();
    }

    /// <summary>
    /// 停止代理
    /// </summary>
    [McpTool(AgentToolNameEnumConstants.AgentStop, "Stop a running agent", AgentToolNameEnumConstants.Agent, ConcurrencySafe = true)]
    public async Task<ToolResult> StopAgentAsync(
        [McpToolParameter("Agent ID or name")] string agent_id,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(agent_id)) {
            var idDiag = AgentDiagnostics.BuildAgentIdEmptyDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(idDiag.FormattedMessage)
                .WithDiagnostic(idDiag)
                .Build();
        }

        var success = await _agentService.StopAgentAsync(agent_id, cancellationToken).ConfigureAwait(false);

        if (!success) {
            var dryState = TryLoadDryRunState(agent_id);
            if (dryState is not null) {
                dryState.Status = "stopped";
                dryState.CompletedAt = _clock.GetUtcNow();
                TrySaveDryRunState(dryState);

                var stopMessage = dryState.IsolationMode == AgentIsolationMode.Worktree.ToValue()
                    ? $"Agent {agent_id} stopped (dry-run, worktree kept — use worktree_remove to clean up)"
                    : $"Agent {agent_id} stopped (dry-run)";
                return ToolResultBuilder.Success()
                    .WithText(stopMessage)
                    .Build();
            }

            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "stop", false);
            var stopDiag = AgentDiagnostics.BuildStopAgentFailedDiagnostic(agent_id);
            return ToolResultBuilder.Error()
                .WithText(stopDiag.FormattedMessage)
                .WithDiagnostic(stopDiag)
                .Build();
        }

        ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "stop", true);
        return ToolResultBuilder.Success()
            .WithText($"Agent {agent_id} stopped")
            .Build();
    }

    /// <summary>
    /// 列出所有运行中的代理 — 同时包含 dry-run 模式的代理
    /// </summary>
    [McpTool(AgentToolNameEnumConstants.AgentRunning, "List all running agents", AgentToolNameEnumConstants.Agent, ConcurrencySafe = true)]
    public async Task<ToolResult> AgentListAsync(
        CancellationToken cancellationToken = default) {
        if (_coordinator == null) {
            var coordDiag = AgentDiagnostics.BuildCoordinatorNotInitializedDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(coordDiag.FormattedMessage)
                .WithDiagnostic(coordDiag)
                .Build();
        }

        try {
            var runningAgents = await _coordinator.GetRunningAgentsAsync(cancellationToken).ConfigureAwait(false);

            var runningList = runningAgents.ToList();
            var response = new System.Text.StringBuilder();

            if (runningList.Count == 0) {
                var dryAgents = ListDryRunAgents();
                if (dryAgents.Count > 0) {
                    response.AppendLine(L.T(StringKey.AgentRunningCount, dryAgents.Count));
                    response.AppendLine();
                    foreach (var dry in dryAgents) {
                        var duration = (_clock.GetUtcNow() - dry.StartedAt).ToString(@"hh\:mm\:ss");
                        response.AppendLine($"- [{dry.Id}] {dry.Description}");
                        response.AppendLine($"  Type: dry-run, Status: {dry.Status}, Duration: {duration}");
                    }
                    ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "list", true);
                    return ToolResultBuilder.Success()
                        .WithText(response.ToString())
                        .Build();
                }
                response.AppendLine(L.T(StringKey.AgentRunningCount, 0));
                response.AppendLine();
                response.AppendLine(L.T(StringKey.AgentNoRunningAgents));
            } else {
                response.AppendLine(L.T(StringKey.AgentRunningCount, runningList.Count));
                response.AppendLine();

                foreach (var agent in runningList) {
                    var duration = agent.StartedAt.HasValue
                        ? (_clock.GetUtcNow() - agent.StartedAt.Value).ToString(@"hh\:mm\:ss")
                        : "unknown";
                    response.AppendLine($"- [{agent.Id}] {agent.Description}");
                    response.AppendLine($"  Type: {agent.Variant?.ToValue() ?? agent.Role.ToValue() ?? "generic"}, Duration: {duration}");
                }
            }

            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "list", true);
            return ToolResultBuilder.Success()
                .WithText(response.ToString())
                .Build();
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger?.LogError(ex, L.T(StringKey.AgentListFailed));
            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "list", false);
            return ToolExceptionDiagnosticHelper.BuildErrorResult("agent_list", ex, _logger);
        }
    }

    /// <summary>
    /// 获取代理的待处理消息
    /// </summary>
    [McpTool(AgentToolNameEnumConstants.AgentGetMessages, "Get pending messages for an agent", AgentToolNameEnumConstants.Agent, ConcurrencySafe = true)]
    public async Task<ToolResult> GetMessagesAsync(
        [McpToolParameter("Agent ID")] string agent_id,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(agent_id)) {
            var idDiag = AgentDiagnostics.BuildAgentIdEmptyDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(idDiag.FormattedMessage)
                .WithDiagnostic(idDiag)
                .Build();
        }

        try {
            var messages = (await _agentService.GetAgentMessagesAsync(agent_id, cancellationToken).ConfigureAwait(false)).ToList();

            if (messages.Count == 0) {
                var dryMsgs = TryLoadDryRunMessages(agent_id);
                if (dryMsgs is not null && dryMsgs.Count > 0) {
                    var dryResponse = new System.Text.StringBuilder();
                    dryResponse.AppendLine($"Pending messages for agent {agent_id}: {dryMsgs.Count}");
                    dryResponse.AppendLine();
                    foreach (var msg in dryMsgs) {
                        dryResponse.AppendLine($"- [text] {msg.Content}");
                        dryResponse.AppendLine($"  Time: {msg.Timestamp:HH:mm:ss}");
                    }
                    return ToolResultBuilder.Success().WithText(dryResponse.ToString()).Build();
                }
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine($"Pending messages for agent {agent_id}: {messages.Count}");
            response.AppendLine();

            if (messages.Count == 0) {
                response.AppendLine("No pending messages.");
            } else {
                foreach (var msg in messages) {
                    response.AppendLine($"- [{msg.MessageType}] {msg.Content}");
                    response.AppendLine($"  From: {msg.FromAgentId}, Time: {msg.Timestamp:HH:mm:ss}");
                }
            }

            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "get_messages", true);
            return ToolResultBuilder.Success()
                .WithText(response.ToString())
                .Build();
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger?.LogError(ex, L.T(StringKey.AgentGetMessagesFailed));
            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "get_messages", false);
            return ToolExceptionDiagnosticHelper.BuildErrorResult("agent_get_messages", ex, _logger);
        }
    }
}
