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

    /// <summary>
    /// 干跑模式 — 不调用 LLM，直接创建 mock agent 并持久化到文件，支持跨进程测试完整链路
    /// </summary>
    [McpToolParameter("Dry run mode: skip LLM, create mock agent for testing (optional)", Required = false)]
    public bool? DryRun { get; init; }
}

/// <summary>
/// Agent 工具处理器 - 创建和管理子代理
/// 通过中间件管道处理验证、fork判断、spawn、流式执行、handoff审查
/// </summary>
[McpToolDispatch(ToolCategory.Agent, Optional = true)]
public partial class AgentToolHandlers
{
    private readonly MiddlewarePipeline<AgentToolContext> _pipeline;
    private readonly IAgentService _agentService;
    private readonly IAgentService? _coordinator;
    private readonly ILogger<AgentToolHandlers>? _logger;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly ITelemetryService? _telemetryService;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ITeamManager? _teamManager;
    private readonly IClockService _clock;

    public AgentToolHandlers(
        MiddlewarePipeline<AgentToolContext> pipeline,
        IAgentService agentService,
        IAgentService? coordinator = null,
        ILogger<AgentToolHandlers>? logger = null,
        ITelemetryService? telemetryService = null,
        IServiceProvider? serviceProvider = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        IClockService? clock = null,
        ITeamManager? teamManager = null)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
        _coordinator = coordinator;
        _logger = logger;
        _telemetryService = telemetryService;
        _serviceProvider = serviceProvider;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _clock = clock ?? SystemClockService.Instance;
        _teamManager = teamManager;
    }

    /// <summary>
    /// 创建并启动子代理
    /// </summary>
    [McpTool(AgentToolNameConstants.Agent, "Create and launch a sub-agent to handle a task", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> CreateAgentAsync(
        [McpToolOptions] AgentCreateOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.DryRun == true)
            return await CreateDryRunAgentAsync(options, cancellationToken).ConfigureAwait(false);

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
            return context.Result ?? ToolResultBuilder.PipelineNoResult();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.AgentCreateFailed));
            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "spawn", false);
            return ToolExceptionDiagnosticHelper.BuildErrorResult("agent", ex, _logger);
        }
    }

    /// <summary>
    /// 干跑模式 — 不调用 LLM，直接创建 mock agent 并持久化到 ~/.jcc/agents/，支持跨进程测试
    /// </summary>
    private async Task<ToolResult> CreateDryRunAgentAsync(AgentCreateOptions options, CancellationToken cancellationToken)
    {
        var agentId = $"agent-dryrun-{Guid.NewGuid():N}"[..^16];
        var now = _clock.GetUtcNow();
        var stateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".jcc", "agents");
#pragma warning disable JCC9001
        Directory.CreateDirectory(stateDir);
#pragma warning restore JCC9001
        var statePath = Path.Combine(stateDir, $"{agentId}.json");
        var state = new DryRunAgentState
        {
            Id = agentId,
            Description = options.Description,
            Status = "running",
            StartedAt = now,
            Prompt = options.Prompt,
        };
#pragma warning disable JCC9001
        await File.WriteAllTextAsync(statePath, RelaxedJsonSerializer.Serialize(state, DryRunAgentStateJsonContext.Default), cancellationToken).ConfigureAwait(false);
#pragma warning restore JCC9001
        var response = new System.Text.StringBuilder();
        response.AppendLine("Agent launched in dry-run mode (no LLM)");
        response.AppendLine($"Agent ID: {agentId}");
        response.AppendLine($"Description: {options.Description}");
        response.AppendLine($"Status: running");
        response.AppendLine();
        response.AppendLine("Use agent_status to query status, agent_stop to stop, agent_get_messages to get messages.");
        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 从 ~/.jcc/agents/{agentId}.json 加载 dry-run agent 状态
    /// </summary>
    private DryRunAgentState? TryLoadDryRunState(string agentId)
    {
        var statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".jcc", "agents", $"{agentId}.json");
#pragma warning disable JCC9001
        if (!File.Exists(statePath))
            return null;
        try
        {
            var json = File.ReadAllText(statePath);
            return RelaxedJsonSerializer.Deserialize(json, DryRunAgentStateJsonContext.Default.DryRunAgentState);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load dry-run agent state for {AgentId}", agentId);
            return null;
        }
#pragma warning restore JCC9001
    }

    /// <summary>
    /// 列出 ~/.jcc/agents/ 目录下所有 dry-run agent 状态文件
    /// </summary>
    private List<DryRunAgentState> ListDryRunAgents()
    {
        var stateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".jcc", "agents");
#pragma warning disable JCC9001
        if (!Directory.Exists(stateDir))
            return [];
        var result = new List<DryRunAgentState>();
        foreach (var file in Directory.EnumerateFiles(stateDir, "*.json"))
        {
            if (file.EndsWith(".messages.json", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var json = File.ReadAllText(file);
                var state = RelaxedJsonSerializer.Deserialize(json, DryRunAgentStateJsonContext.Default.DryRunAgentState);
                if (state is not null)
                    result.Add(state);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load dry-run agent state from {File}", file);
            }
        }
        return result;
#pragma warning restore JCC9001
    }

    /// <summary>
    /// 保存 dry-run agent 状态到 ~/.jcc/agents/{agentId}.json
    /// </summary>
    private void TrySaveDryRunState(DryRunAgentState state)
    {
        var stateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".jcc", "agents");
#pragma warning disable JCC9001
        Directory.CreateDirectory(stateDir);
        var statePath = Path.Combine(stateDir, $"{state.Id}.json");
        try
        {
            File.WriteAllText(statePath, RelaxedJsonSerializer.Serialize(state, DryRunAgentStateJsonContext.Default));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save dry-run agent state for {AgentId}", state.Id);
        }
#pragma warning restore JCC9001
    }

    /// <summary>
    /// 追加 dry-run agent 消息到 ~/.jcc/agents/{agentId}.messages.json
    /// </summary>
    private void AppendDryRunMessage(string agentId, string content, string? summary)
    {
        var msgDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".jcc", "agents");
#pragma warning disable JCC9001
        Directory.CreateDirectory(msgDir);
        var msgPath = Path.Combine(msgDir, $"{agentId}.messages.json");
        var msgs = new List<DryRunAgentMessage>();
        if (File.Exists(msgPath))
        {
            try
            {
                var existing = File.ReadAllText(msgPath);
                var loaded3 = RelaxedJsonSerializer.Deserialize(existing, DryRunAgentStateJsonContext.Default.ListDryRunAgentMessage);
                if (loaded3 is not null)
                    msgs = [.. loaded3];
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load dry-run messages for {AgentId}", agentId);
            }
        }
        msgs.Add(new DryRunAgentMessage { Content = content, Summary = summary, Timestamp = _clock.GetUtcNow() });
        try
        {
            File.WriteAllText(msgPath, RelaxedJsonSerializer.Serialize(msgs, DryRunAgentStateJsonContext.Default));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save dry-run messages for {AgentId}", agentId);
        }
#pragma warning restore JCC9001
    }

    /// <summary>
    /// 从 ~/.jcc/agents/{agentId}.messages.json 加载 dry-run agent 消息
    /// </summary>
    private List<DryRunAgentMessage>? TryLoadDryRunMessages(string agentId)
    {
        var msgPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".jcc", "agents", $"{agentId}.messages.json");
#pragma warning disable JCC9001
        if (!File.Exists(msgPath))
            return null;
        try
        {
            var json = File.ReadAllText(msgPath);
            return RelaxedJsonSerializer.Deserialize(json, DryRunAgentStateJsonContext.Default.ListDryRunAgentMessage)?.ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load dry-run messages for {AgentId}", agentId);
            return null;
        }
#pragma warning restore JCC9001
    }

    /// <summary>
    /// 列出可用的代理类型
    /// </summary>
    [McpTool(AgentToolNameConstants.AgentList, "List available agent types", AgentToolNameConstants.Agent, ConcurrencySafe = true)]
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
    [McpTool(AgentToolNameConstants.AgentStatus, "Get agent status", AgentToolNameConstants.Agent, ConcurrencySafe = true)]
    public async Task<ToolResult> GetAgentStatusAsync(
        [McpToolParameter("Agent ID or name")] string agent_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_id))
        {
            var idDiag = BuildAgentIdEmptyDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(idDiag.FormattedMessage)
                .WithDiagnostic(idDiag)
                .Build();
        }

        var agent = await _agentService.GetAgentAsync(agent_id, cancellationToken).ConfigureAwait(false);

        if (agent == null)
        {
            var dryState = TryLoadDryRunState(agent_id);
            if (dryState is not null)
            {
                var dryResponse = new System.Text.StringBuilder();
                dryResponse.AppendLine($"Agent status: {dryState.Status}");
                dryResponse.AppendLine($"Agent ID: {dryState.Id}");
                dryResponse.AppendLine($"Description: {dryState.Description}");
                dryResponse.AppendLine($"Started at: {dryState.StartedAt:yyyy-MM-dd HH:mm:ss}");
                if (dryState.CompletedAt.HasValue)
                    dryResponse.AppendLine($"Completed at: {dryState.CompletedAt.Value:yyyy-MM-dd HH:mm:ss}");
                if (!string.IsNullOrEmpty(dryState.Prompt))
                    dryResponse.AppendLine($"Prompt: {dryState.Prompt}");
                return ToolResultBuilder.Success().WithText(dryResponse.ToString()).Build();
            }

            var notFoundDiag = BuildAgentNotFoundDiagnostic(agent_id);
            return ToolResultBuilder.Error()
                .WithText(notFoundDiag.FormattedMessage)
                .WithDiagnostic(notFoundDiag)
                .Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"Agent status: {agent.Status}");
        response.AppendLine($"Agent ID: {agent.Id}");
        response.AppendLine($"Description: {agent.Description}");

        if (!string.IsNullOrEmpty(agent.Role.ToValue()))
        {
            response.AppendLine($"Type: {agent.Variant?.ToValue() ?? agent.Role.ToValue()}");
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
    [McpTool(AgentToolNameConstants.AgentStop, "Stop a running agent", AgentToolNameConstants.Agent, ConcurrencySafe = true)]
    public async Task<ToolResult> StopAgentAsync(
        [McpToolParameter("Agent ID or name")] string agent_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_id))
        {
            var idDiag = BuildAgentIdEmptyDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(idDiag.FormattedMessage)
                .WithDiagnostic(idDiag)
                .Build();
        }

        var success = await _agentService.StopAgentAsync(agent_id, cancellationToken).ConfigureAwait(false);

        if (!success)
        {
            var dryState = TryLoadDryRunState(agent_id);
            if (dryState is not null)
            {
                dryState.Status = "stopped";
                dryState.CompletedAt = _clock.GetUtcNow();
                TrySaveDryRunState(dryState);
                return ToolResultBuilder.Success()
                    .WithText($"Agent {agent_id} stopped (dry-run)")
                    .Build();
            }

            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "stop", false);
            var stopDiag = BuildStopAgentFailedDiagnostic(agent_id);
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

    [McpTool(AgentToolNameConstants.AgentRunning, "List all running agents", AgentToolNameConstants.Agent, ConcurrencySafe = true)]
    public async Task<ToolResult> AgentListAsync(
        CancellationToken cancellationToken = default)
    {
        if (_coordinator == null)
        {
            var coordDiag = BuildCoordinatorNotInitializedDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(coordDiag.FormattedMessage)
                .WithDiagnostic(coordDiag)
                .Build();
        }

        try
        {
            var runningAgents = await _coordinator.GetRunningAgentsAsync(cancellationToken).ConfigureAwait(false);

            var runningList = runningAgents.ToList();
            var response = new System.Text.StringBuilder();

            if (runningList.Count == 0)
            {
                var dryAgents = ListDryRunAgents();
                if (dryAgents.Count > 0)
                {
                    response.AppendLine(L.T(StringKey.AgentRunningCount, dryAgents.Count));
                    response.AppendLine();
                    foreach (var dry in dryAgents)
                    {
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
            }
            else
            {
                response.AppendLine(L.T(StringKey.AgentRunningCount, runningList.Count));
                response.AppendLine();

                foreach (var agent in runningList)
                {
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
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.AgentListFailed));
            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "list", false);
            return ToolExceptionDiagnosticHelper.BuildErrorResult("agent_list", ex, _logger);
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
            var recipientDiag = BuildRecipientEmptyDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(recipientDiag.FormattedMessage)
                .WithDiagnostic(recipientDiag)
                .Build();
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            var msgDiag = BuildMessageEmptyDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(msgDiag.FormattedMessage)
                .WithDiagnostic(msgDiag)
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
                    var broadcastDiag = BuildBroadcastStructuredMessageDiagnostic(structuredData.Type.ToValue());
                    return ToolResultBuilder.Error()
                        .WithText(broadcastDiag.FormattedMessage)
                        .WithDiagnostic(broadcastDiag)
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
                var dryState = TryLoadDryRunState(to);
                if (dryState is not null)
                {
                    AppendDryRunMessage(to, message, summary);
                    var dryMsg = summary is not null
                        ? $"Message sent to {to}: {summary}"
                        : $"Message sent to {to}";
                    return ToolResultBuilder.Success().WithText(dryMsg).Build();
                }

                ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "send_message", false);
                var sendDiag = BuildSendMessageFailedDiagnostic(to);
                return ToolResultBuilder.Error()
                    .WithText(sendDiag.FormattedMessage)
                    .WithDiagnostic(sendDiag)
                    .Build();
            }

            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "send_message", true);

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
            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "send_message", false);
            return ToolExceptionDiagnosticHelper.BuildErrorResult("agent_send_message", ex, _logger, "to", to);
        }
    }

    /// <summary>
    /// 处理广播消息 — 对齐 TS SendMessageTool handleBroadcast
    /// </summary>
    private async Task<ToolResult> HandleBroadcastAsync(string message, string? summary, CancellationToken cancellationToken)
    {
        // 通过 TeamManager 广播
        var teamManager = _teamManager ?? _serviceProvider?.GetService(typeof(ITeamManager)) as ITeamManager;
        if (teamManager is null)
        {
            var svcDiag = BuildBroadcastServiceUnavailableDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(svcDiag.FormattedMessage)
                .WithDiagnostic(svcDiag)
                .Build();
        }

        var teams = await teamManager.ListTeamsAsync(cancellationToken).ConfigureAwait(false);
        if (teams.Count == 0)
        {
            var noTeamsDiag = BuildBroadcastNoTeamsDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(noTeamsDiag.FormattedMessage)
                .WithDiagnostic(noTeamsDiag)
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

        ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "broadcast", sentCount > 0);
        return ToolResultBuilder.Success()
            .WithText($"Broadcast sent to {sentCount} team(s){(summary is not null ? $": {summary}" : "")}")
            .Build();
    }

    /// <summary>
    /// 获取代理的待处理消息
    /// </summary>
    /// <summary>
    /// 将用户输入转发给运行中的子代理 — 主代理 LLM 协同决策时调用
    /// 用户在子代理运行期间追加的输入，通过 IAgentInputForwardQueue 入队，子代理每轮 LLM 调用前主动消费
    /// </summary>
    [McpTool(AgentToolNameConstants.ForwardUserInput, "Forward user input to a running sub-agent", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> ForwardUserInputAsync(
        [McpToolParameter("Target sub-agent ID")] string agentId,
        [McpToolParameter("User input to forward")] string userInput,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return ToolResultBuilder.Error()
                .WithText("agentId 不能为空")
                .Build();
        }

        if (string.IsNullOrWhiteSpace(userInput))
        {
            return ToolResultBuilder.Error()
                .WithText("userInput 不能为空")
                .Build();
        }

        try
        {
            var dryState = TryLoadDryRunState(agentId);
            if (dryState is not null)
            {
                AppendDryRunMessage(agentId, userInput, "forwarded_user_input");
                return ToolResultBuilder.Success()
                    .WithText($"用户输入已转发给子代理 {agentId}")
                    .Build();
            }

            var sent = await _agentService.ForwardUserInputToAgentAsync(agentId, userInput, cancellationToken).ConfigureAwait(false);

            if (!sent)
            {
                ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "forward_user_input", false);
                return ToolResultBuilder.Error()
                    .WithText($"转发失败：子代理 {agentId} 不存在或转发队列未注册")
                    .Build();
            }

            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "forward_user_input", true);
            return ToolResultBuilder.Success()
                .WithText($"用户输入已转发给子代理 {agentId}")
                .Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ForwardUserInput failed for agent {AgentId}", agentId);
            return ToolResultBuilder.Error()
                .WithText($"转发异常: {ex.Message}")
                .Build();
        }
    }

    [McpTool(AgentToolNameConstants.AgentGetMessages, "Get pending messages for an agent", AgentToolNameConstants.Agent, ConcurrencySafe = true)]
    public async Task<ToolResult> GetMessagesAsync(
        [McpToolParameter("Agent ID")] string agent_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent_id))
        {
            var idDiag = BuildAgentIdEmptyDiagnostic();
            return ToolResultBuilder.Error()
                .WithText(idDiag.FormattedMessage)
                .WithDiagnostic(idDiag)
                .Build();
        }

        try
        {
            var messages = (await _agentService.GetAgentMessagesAsync(agent_id, cancellationToken).ConfigureAwait(false)).ToList();

            if (messages.Count == 0)
            {
                var dryMsgs = TryLoadDryRunMessages(agent_id);
                if (dryMsgs is not null && dryMsgs.Count > 0)
                {
                    var dryResponse = new System.Text.StringBuilder();
                    dryResponse.AppendLine($"Pending messages for agent {agent_id}: {dryMsgs.Count}");
                    dryResponse.AppendLine();
                    foreach (var msg in dryMsgs)
                    {
                        dryResponse.AppendLine($"- [text] {msg.Content}");
                        dryResponse.AppendLine($"  Time: {msg.Timestamp:HH:mm:ss}");
                    }
                    return ToolResultBuilder.Success().WithText(dryResponse.ToString()).Build();
                }
            }

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

            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "get_messages", true);
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
            ToolTelemetryHelper.RecordToolCount(_telemetryService, "agent.handler.count", "get_messages", false);
            return ToolExceptionDiagnosticHelper.BuildErrorResult("agent_get_messages", ex, _logger);
        }
    }

    #region Diagnostic Builders

    /// <summary>
    /// 构建 agent_id 为空的结构化诊断。
    /// 适用于 GetAgentStatusAsync、StopAgentAsync、GetMessagesAsync 的参数验证。
    /// </summary>
    internal static ToolDiagnostic BuildAgentIdEmptyDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "AgentIdEmpty",
            formattedMessage: "agent_id cannot be empty",
            details:
            [
                new DiagnosticDetail("Param", "agent_id"),
            ],
            suggestions:
            [
                "提供有效的代理 ID 或名称。",
            ]);
    }

    /// <summary>
    /// 构建代理未找到的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildAgentNotFoundDiagnostic(string agentId)
    {
        return ToolDiagnostic.Create(
            reason: "AgentNotFound",
            formattedMessage: $"Agent not found: {agentId}",
            details:
            [
                new DiagnosticDetail("AgentId", agentId),
            ],
            suggestions:
            [
                "使用 agent_list 查看运行中的代理。",
                "确认代理 ID 或名称拼写正确。",
            ]);
    }

    /// <summary>
    /// 构建停止代理失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildStopAgentFailedDiagnostic(string agentId)
    {
        return ToolDiagnostic.Create(
            reason: "AgentStopFailed",
            formattedMessage: $"Failed to stop agent or agent not found: {agentId}",
            details:
            [
                new DiagnosticDetail("AgentId", agentId),
            ],
            suggestions:
            [
                "使用 agent_list 查看运行中的代理。",
                "确认代理 ID 或名称拼写正确。",
                "代理可能已完成或已停止。",
            ]);
    }

    /// <summary>
    /// 构建代理协调器未初始化的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildCoordinatorNotInitializedDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "AgentCoordinatorNotInitialized",
            formattedMessage: L.T(StringKey.AgentCoordinatorNotInitialized),
            details:
            [
                new DiagnosticDetail("Component", "IAgentService"),
            ],
            suggestions:
            [
                "确认 AgentToolHandlers 构造时传入了 IAgentService 实例。",
            ]);
    }

    /// <summary>
    /// 构建消息接收者为空的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildRecipientEmptyDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "AgentRecipientEmpty",
            formattedMessage: "Recipient (to) cannot be empty",
            details:
            [
                new DiagnosticDetail("Param", "to"),
            ],
            suggestions:
            [
                "提供接收者名称、代理 ID 或 * 进行广播。",
            ]);
    }

    /// <summary>
    /// 构建消息内容为空的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildMessageEmptyDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "AgentMessageEmpty",
            formattedMessage: "message cannot be empty",
            details:
            [
                new DiagnosticDetail("Param", "message"),
            ],
            suggestions:
            [
                "提供要发送的消息内容。",
            ]);
    }

    /// <summary>
    /// 构建结构化消息不支持广播的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildBroadcastStructuredMessageDiagnostic(string messageType)
    {
        return ToolDiagnostic.Create(
            reason: "AgentBroadcastStructuredMessage",
            formattedMessage: $"Cannot broadcast structured message (type: {messageType}). Send to a specific teammate instead.",
            details:
            [
                new DiagnosticDetail("MessageType", messageType),
                new DiagnosticDetail("Recipient", "*"),
            ],
            suggestions:
            [
                "将结构化消息发送给特定的 teammate，而非使用 * 广播。",
            ]);
    }

    /// <summary>
    /// 构建发送消息失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildSendMessageFailedDiagnostic(string recipient)
    {
        return ToolDiagnostic.Create(
            reason: "AgentSendMessageFailed",
            formattedMessage: $"Failed to send message: agent '{recipient}' not found or messaging service unavailable",
            details:
            [
                new DiagnosticDetail("Recipient", recipient),
            ],
            suggestions:
            [
                "使用 agent_list 查看运行中的代理。",
                "确认接收者名称或代理 ID 正确。",
                "检查消息服务是否可用。",
            ]);
    }

    /// <summary>
    /// 构建广播服务不可用的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildBroadcastServiceUnavailableDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "AgentBroadcastServiceUnavailable",
            formattedMessage: "Broadcast failed: team service not available",
            details:
            [
                new DiagnosticDetail("Component", "ITeamManager"),
            ],
            suggestions:
            [
                "确认 ITeamManager 已在 DI 容器中注册。",
            ]);
    }

    /// <summary>
    /// 构建广播无团队的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildBroadcastNoTeamsDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "AgentBroadcastNoTeams",
            formattedMessage: "Broadcast failed: no teams exist",
            details: [],
            suggestions:
            [
                "先创建团队后再发送广播消息。",
            ]);
    }

    #endregion

}

/// <summary>
/// Dry-run agent 持久化状态 — 跨进程共享 mock agent 状态
/// </summary>
public sealed class DryRunAgentState
{
    public required string Id { get; set; }
    public required string Description { get; set; }
    public required string Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Prompt { get; set; }
}

/// <summary>
/// Dry-run agent 消息 — 跨进程共享 mock agent 消息
/// </summary>
public sealed class DryRunAgentMessage
{
    public required string Content { get; set; }
    public string? Summary { get; set; }
    public DateTime Timestamp { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DryRunAgentState))]
[JsonSerializable(typeof(List<DryRunAgentMessage>))]
internal sealed partial class DryRunAgentStateJsonContext : JsonSerializerContext;
