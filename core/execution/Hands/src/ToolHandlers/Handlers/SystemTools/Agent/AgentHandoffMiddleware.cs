namespace Tools.Handlers;

/// <summary>
/// Agent Handoff 安全审查中间件 — 子智能体完成后安全审查
/// 对齐 TS classifyHandoffIfNeeded — 在 auto 模式下审查子智能体的操作是否违反安全策略
/// 对齐 TS cleanupWorktreeIfNeeded — 输出 worktree 信息（worktreePath/branch）
/// </summary>
[Register]
public sealed partial class AgentHandoffMiddleware : IAgentToolMiddleware
{
    [Inject] private readonly IHandoffClassifier? _handoffClassifier;
    [Inject] private readonly JoinCode.Abstractions.Interfaces.IAgentWorktreeManager? _worktreeManager;
    [Inject] private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly ILogger<AgentHandoffMiddleware>? _logger;

    /// <inheritdoc />
    public int Order => 500;

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public async Task InvokeAsync(AgentToolContext context, MiddlewareDelegate<AgentToolContext> next, CancellationToken ct)
    {
        // 执行 handoff 安全审查
        var handoffInfo = new AgentInfo
        {
            Id = context.AgentId ?? "unknown",
            Description = context.Description,
            AgentType = context.SubagentType
        };
        var handoffResult = new JoinCode.Abstractions.Interfaces.AgentResult
        {
            AgentId = context.AgentId ?? "unknown",
            Success = context.Succeeded,
            Output = context.Succeeded ? context.ContentBuilder.ToString() : string.Empty,
            Error = context.ErrorMessage
        };
        context.HandoffWarning = await ClassifyHandoffAsync(handoffInfo, handoffResult, ct).ConfigureAwait(false);

        // 构建最终响应
        var responseBuilder = new StringBuilder();
        responseBuilder.AppendLine("Agent started");
        responseBuilder.AppendLine($"Agent ID: {context.AgentId}");
        responseBuilder.AppendLine($"Description: {context.Description}");

        if (!string.IsNullOrEmpty(context.SubagentType))
        {
            responseBuilder.AppendLine($"Type: {context.SubagentType}");
        }

        // 对齐 TS: worktree 信息输出 — 当 agent 使用 worktree 隔离时输出路径和分支
        await AppendWorktreeInfoAsync(context, responseBuilder, ct).ConfigureAwait(false);

        responseBuilder.AppendLine();
        responseBuilder.AppendLine("Agent execution result:");

        if (context.Succeeded)
        {
            if (context.HandoffWarning is not null)
            {
                responseBuilder.AppendLine(context.HandoffWarning);
                responseBuilder.AppendLine();
            }
            responseBuilder.AppendLine(context.ContentBuilder.ToString());
        }
        else
        {
            responseBuilder.AppendLine($"[Failed] {context.ErrorMessage}");
        }

        // 对齐 TS ONE_SHOT_BUILTIN_AGENT_TYPES — 一次性代理省略 agentId/SendMessage 提示
        var isOneShot = !string.IsNullOrEmpty(context.SubagentType) && OneShotBuiltinAgentTypes.IsOneShot(context.SubagentType);
        if (!isOneShot)
        {
            responseBuilder.AppendLine();
            responseBuilder.AppendLine($"Agent ID: {context.AgentId}");
            responseBuilder.AppendLine($"Use {AgentToolName.AgentSendMessage.ToValue()} to continue this agent by providing the agent ID.");
        }

        RecordAgentMetrics("spawn", true);
        context.Result = ToolResultBuilder.Success()
            .WithText(responseBuilder.ToString())
            .Build();

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 子智能体交接安全审查 — 对齐 TS classifyHandoffIfNeeded
    /// 在 auto 模式下审查子智能体的操作是否违反安全策略
    /// </summary>
    private async Task<string?> ClassifyHandoffAsync(AgentInfo agentInfo, JoinCode.Abstractions.Interfaces.AgentResult agentResult, CancellationToken cancellationToken)
    {
        if (_handoffClassifier is null) return null;

        try
        {
            // 构建工具调用记录 — 从 AgentResult 中提取
            // 当前 AgentResult 不包含详细工具调用记录，使用简化版审查
            var request = new HandoffClassificationRequest
            {
                AgentId = agentInfo.Id,
                AgentType = agentInfo.AgentType,
                ToolInvocations = [], // AgentResult 暂无工具调用明细
                PermissionMode = PermissionMode.Auto, // 默认 auto 模式审查
                TotalToolUseCount = 0
            };

            var result = await _handoffClassifier.ClassifyAsync(request, cancellationToken).ConfigureAwait(false);
            return result?.WarningMessage;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 对齐 TS: 分类器不可用时温和提示，不阻塞输出
            _logger?.LogWarning(ex, "Handoff classifier failed for agent {AgentId}", agentInfo.Id);
            return "Note: The safety classifier was unavailable when reviewing this sub-agent's work. Please carefully verify the sub-agent's actions and output before acting on them.";
        }
    }

    private void RecordAgentMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("agent.handler.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, description: "Agent handler count");

    /// <summary>
    /// 追加 worktree 信息到响应 — 对齐 TS LocalAgentTask worktreeSection
    /// </summary>
    private async Task AppendWorktreeInfoAsync(AgentToolContext context, StringBuilder responseBuilder, CancellationToken ct)
    {
        if (context.AgentId is null || _worktreeManager is null || !_worktreeManager.IsWorktreeIsolationEnabled)
            return;

        try
        {
            var session = await _worktreeManager.GetWorktreeSessionAsync(context.AgentId, ct).ConfigureAwait(false);
            if (session is not null)
            {
                context.WorktreePath = session.WorktreePath;
                context.WorktreeBranch = session.BranchName;

                responseBuilder.AppendLine($"worktreePath: {session.WorktreePath}");
                responseBuilder.AppendLine($"worktreeBranch: {session.BranchName}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get worktree info for agent {AgentId}", context.AgentId);
        }
    }
}
