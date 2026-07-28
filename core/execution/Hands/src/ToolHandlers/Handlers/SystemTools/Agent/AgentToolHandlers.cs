namespace Tools.Handlers;

/// <summary>
/// 创建子代理的选项参数
/// </summary>
public sealed record AgentCreateOptions
{
    [McpToolParameter("Agent description (3-5 words)")]
    public required string Description { get; init; }

    [McpToolParameter("Task prompt/instructions")]
    public required string Prompt { get; init; }

    [McpToolParameter("Agent type (optional)", Required = false)]
    public string? SubagentType { get; init; }

    [McpToolParameter("Model override: sonnet/opus/haiku (optional)", Required = false)]
    public string? Model { get; init; }

    [McpToolParameter("Agent name for SendMessage addressing (optional)", Required = false)]
    public string? Name { get; init; }

    [McpToolParameter("Run in background", Required = false)]
    public bool? RunInBackground { get; init; } = false;

    [McpToolParameter("Isolation mode: none/worktree (optional)", Required = false)]
    public string? Isolation { get; init; } = "none";

    [McpToolParameter("Working directory override (optional)", Required = false)]
    public string? Cwd { get; init; }

    [McpToolParameter("Memory scope: user/project/local (optional, enables agent memory)", Required = false)]
    public string? Memory { get; init; }
}

/// <summary>
/// Agent 工具处理器 - 创建和管理子代理
/// 通过中间件管道处理验证、fork判断、spawn、流式执行、handoff审查
/// </summary>
[McpToolHandler(ToolCategory.Agent, Optional = true)]
public partial class AgentToolHandlers
{
    private readonly MiddlewarePipeline<AgentToolContext> _pipeline;
    private readonly IAgentService _agentService;
    private readonly IAgentCoordinator? _coordinator;
    [Inject] private readonly ILogger<AgentToolHandlers>? _logger;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly ITelemetryService? _telemetryService;
    private readonly IServiceProvider? _serviceProvider;
    private readonly IClockService _clock;

    public AgentToolHandlers(
        MiddlewarePipeline<AgentToolContext> pipeline,
        IAgentService agentService,
        IAgentCoordinator? coordinator = null,
        ILogger<AgentToolHandlers>? logger = null,
        ITelemetryService? telemetryService = null,
        IServiceProvider? serviceProvider = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        IClockService? clock = null)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
        _coordinator = coordinator;
        _logger = logger;
        _telemetryService = telemetryService;
        _serviceProvider = serviceProvider;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 创建并启动子代理
    /// </summary>
    [McpTool(AgentToolNameConstants.Agent, "Create and launch a sub-agent to handle a task", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> CreateAgentAsync(
        [McpToolOptions] AgentCreateOptions options,
        CancellationToken cancellationToken = default)
    {
        var context = new AgentToolContext
        {
            Description = options.Description,
            Prompt = options.Prompt,
            SubagentType = options.SubagentType,
            Model = options.Model,
            Name = options.Name,
            RunInBackground = options.RunInBackground,
            Isolation = options.Isolation,
            Cwd = options.Cwd,
            Memory = options.Memory,
        };

        try
        {
            await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            return context.Result ?? ToolResultBuilder.Error().WithText("Pipeline did not produce a result").Build();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.AgentCreateFailed));
            RecordAgentMetrics("spawn", false);
            return ToolResultBuilder.Error()
                .WithText($"Failed to create agent: {ex.Message}")
                .Build();
        }
    }

    /// <summary>
    /// 列出可用的代理类型
    /// </summary>
    [McpTool(AgentToolNameConstants.AgentList, "List available agent types", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> ListAgentTypesAsync(
        CancellationToken cancellationToken = default)
    {
        var types = await _agentService.GetAvailableAgentTypesAsync(cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine("Available agent types:");
        response.AppendLine();

        if (types.Count == 0)
        {
            response.AppendLine("No predefined agent types available. You can use the generic agent.");
        }
        else
        {
            response.AppendLine(string.Join("\n", types.Select(type =>
            {
                var lines = new List<string> { $"- {type.Name}: {type.Description}" };
                if (type.AvailableTools?.Count > 0)
                {
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
    [McpTool(AgentToolNameConstants.AgentStatus, "Get agent status", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> GetAgentStatusAsync(
        [McpToolParameter("Agent ID or name")] string agent_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_id))
        {
            return ToolResultBuilder.Error()
                .WithText("agent_id cannot be empty")
                .Build();
        }

        var agent = await _agentService.GetAgentAsync(agent_id, cancellationToken).ConfigureAwait(false);

        if (agent == null)
        {
            return ToolResultBuilder.Error()
                .WithText($"Agent not found: {agent_id}")
                .Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"Agent status: {agent.Status}");
        response.AppendLine($"Agent ID: {agent.Id}");
        response.AppendLine($"Description: {agent.Description}");

        if (!string.IsNullOrEmpty(agent.AgentType))
        {
            response.AppendLine($"Type: {agent.AgentType}");
        }

        if (agent.StartedAt.HasValue)
        {
            response.AppendLine($"Started at: {agent.StartedAt.Value:yyyy-MM-dd HH:mm:ss}");
        }

        if (agent.CompletedAt.HasValue)
        {
            response.AppendLine($"Completed at: {agent.CompletedAt.Value:yyyy-MM-dd HH:mm:ss}");
        }

        if (!string.IsNullOrEmpty(agent.Output))
        {
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
    [McpTool(AgentToolNameConstants.AgentStop, "Stop a running agent", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> StopAgentAsync(
        [McpToolParameter("Agent ID or name")] string agent_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_id))
        {
            return ToolResultBuilder.Error()
                .WithText("agent_id cannot be empty")
                .Build();
        }

        var success = await _agentService.StopAgentAsync(agent_id, cancellationToken).ConfigureAwait(false);

        if (!success)
        {
            RecordAgentMetrics("stop", false);
            return ToolResultBuilder.Error()
                .WithText($"Failed to stop agent or agent not found: {agent_id}")
                .Build();
        }

        RecordAgentMetrics("stop", true);
        return ToolResultBuilder.Success()
            .WithText($"Agent {agent_id} stopped")
            .Build();
    }

    [McpTool(AgentToolNameConstants.AgentRunning, "List all running agents", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> AgentListAsync(
        CancellationToken cancellationToken = default)
    {
        if (_coordinator == null)
            return ToolResultBuilder.Error()
                .WithText(L.T(StringKey.AgentCoordinatorNotInitialized))
                .Build();

        try
        {
            var runningAgents = await _coordinator.GetRunningAgentsAsync(cancellationToken).ConfigureAwait(false);

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.AgentRunningCount, runningAgents.Count));
            response.AppendLine();

            if (runningAgents.Count == 0)
            {
                response.AppendLine(L.T(StringKey.AgentNoRunningAgents));
            }
            else
            {
                foreach (var agent in runningAgents)
                {
                    var duration = agent.StartedAt.HasValue
                        ? (_clock.GetUtcNow() - agent.StartedAt.Value).ToString(@"hh\:mm\:ss")
                        : "unknown";
                    response.AppendLine($"- [{agent.Id}] {agent.Description}");
                    response.AppendLine($"  Type: {agent.AgentType ?? "generic"}, Duration: {duration}");
                }
            }

            RecordAgentMetrics("list", true);
            return ToolResultBuilder.Success()
                .WithText(response.ToString())
                .Build();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.AgentListFailed));
            RecordAgentMetrics("list", false);
            return ToolResultBuilder.Error()
                .WithText($"Failed to list agents: {ex.Message}")
                .Build();
        }
    }

    /// <summary>
    /// 向运行中的代理发送消息 — 对齐 TS SendMessageTool
    /// 支持: 按名称/ID发送、广播(to="*")、结构化消息(shutdown_request/plan_approval_response)
    /// </summary>
    [McpTool(AgentToolNameConstants.AgentSendMessage, "Send a message to another agent", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> SendMessageAsync(
        [McpToolParameter("Recipient: teammate name, agent ID, or '*' for broadcast")] string to,
        [McpToolParameter("Message content (plain text or structured JSON)")] string message,
        [McpToolParameter("5-10 word summary preview (required for plain text messages)", Required = false)] string? summary = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            return ToolResultBuilder.Error()
                .WithText("Recipient (to) cannot be empty")
                .Build();
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return ToolResultBuilder.Error()
                .WithText("message cannot be empty")
                .Build();
        }

        try
        {
            // 解析结构化消息 — 对齐 TS SendMessageTool validateInput
            var isStructured = StructuredMessageParser.TryParse(message, out var structuredData);

            // 结构化消息验证 — 对齐 TS validateInput 规则
            if (isStructured && structuredData is not null)
            {
                // 结构化消息不能广播
                if (to == "*")
                {
                    return ToolResultBuilder.Error()
                        .WithText($"Cannot broadcast structured message (type: {structuredData.Type.ToValue()}). Send to a specific teammate instead.")
                        .Build();
                }

                // shutdown_response 必须发给 team-lead
                if (structuredData.Type == TeammateMessageType.ShutdownApproved ||
                    structuredData.Type == TeammateMessageType.ShutdownRejected)
                {
                    // 对齐 TS: shutdown_response 必须发给 TEAM_LEAD_NAME
                    // 此处仅记录日志，不强制阻止（C# 端 team-lead 名称可能不同）
                    _logger?.LogDebug("Shutdown response sent to {Recipient}", to);
                }
            }
            else
            {
                // 纯文本消息验证 — 对齐 TS: summary 推荐但不强制
                // TS 中 summary 是 "required for plain text messages"，C# 端作为可选参数
                if (string.IsNullOrEmpty(summary))
                {
                    _logger?.LogWarning("Agent message sent without summary");
                }
            }

            // 广播模式: to="*"（仅纯文本消息可到达此处）
            if (to == "*")
            {
                var broadcastResult = await HandleBroadcastAsync(message, summary, cancellationToken).ConfigureAwait(false);
                return broadcastResult;
            }

            // 点对点消息 — 传入结构化消息数据
            var sent = isStructured && structuredData is not null
                ? await _agentService.SendStructuredMessageAsync(to, structuredData, message, cancellationToken).ConfigureAwait(false)
                : await _agentService.SendMessageToAgentAsync(to, message, cancellationToken).ConfigureAwait(false);

            if (!sent)
            {
                RecordAgentMetrics("send_message", false);
                return ToolResultBuilder.Error()
                    .WithText($"Failed to send message: agent '{to}' not found or messaging service unavailable")
                    .Build();
            }

            RecordAgentMetrics("send_message", true);

            // 结构化消息的响应格式 — 对齐 TS RequestOutput/ResponseOutput
            if (isStructured && structuredData is not null)
            {
                var typeStr = structuredData.Type.ToValue();
                return structuredData.Type switch
                {
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
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.AgentSendMessageFailed));
            RecordAgentMetrics("send_message", false);
            return ToolResultBuilder.Error()
                .WithText($"Failed to send message: {ex.Message}")
                .Build();
        }
    }

    /// <summary>
    /// 处理广播消息 — 对齐 TS SendMessageTool handleBroadcast
    /// </summary>
    private async Task<ToolResult> HandleBroadcastAsync(string message, string? summary, CancellationToken cancellationToken)
    {
        // 通过 TeamManager 广播
        var teamManager = _serviceProvider?.GetService(typeof(ITeamManager)) as ITeamManager;
        if (teamManager is null)
        {
            return ToolResultBuilder.Error()
                .WithText("Broadcast failed: team service not available")
                .Build();
        }

        var teams = await teamManager.ListTeamsAsync(cancellationToken).ConfigureAwait(false);
        if (teams.Count == 0)
        {
            return ToolResultBuilder.Error()
                .WithText("Broadcast failed: no teams exist")
                .Build();
        }

        var currentAgentId = _subAgentContextAccessor.Current?.AgentId ?? "unknown";
        var sentCount = 0;

        foreach (var team in teams)
        {
            var result = await teamManager.BroadcastMessageAsync(
                team.TeamId, currentAgentId, message, null, cancellationToken).ConfigureAwait(false);
            if (result.Success) sentCount++;
        }

        RecordAgentMetrics("broadcast", sentCount > 0);
        return ToolResultBuilder.Success()
            .WithText($"Broadcast sent to {sentCount} team(s){(summary is not null ? $": {summary}" : "")}")
            .Build();
    }

    /// <summary>
    /// 获取代理的待处理消息
    /// </summary>
    [McpTool(AgentToolNameConstants.AgentGetMessages, "Get pending messages for an agent", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> GetMessagesAsync(
        [McpToolParameter("Agent ID")] string agent_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_id))
        {
            return ToolResultBuilder.Error()
                .WithText("agent_id cannot be empty")
                .Build();
        }

        try
        {
            var messages = await _agentService.GetAgentMessagesAsync(agent_id, cancellationToken).ConfigureAwait(false);

            var response = new System.Text.StringBuilder();
            response.AppendLine($"Pending messages for agent {agent_id}: {messages.Count}");
            response.AppendLine();

            if (messages.Count == 0)
            {
                response.AppendLine("No pending messages.");
            }
            else
            {
                foreach (var msg in messages)
                {
                    response.AppendLine($"- [{msg.MessageType}] {msg.Content}");
                    response.AppendLine($"  From: {msg.FromAgentId}, Time: {msg.Timestamp:HH:mm:ss}");
                }
            }

            RecordAgentMetrics("get_messages", true);
            return ToolResultBuilder.Success()
                .WithText(response.ToString())
                .Build();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.AgentGetMessagesFailed));
            RecordAgentMetrics("get_messages", false);
            return ToolResultBuilder.Error()
                .WithText($"Failed to get messages: {ex.Message}")
                .Build();
        }
    }

    private void RecordAgentMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("agent.handler.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, description: "Agent handler count");
}
