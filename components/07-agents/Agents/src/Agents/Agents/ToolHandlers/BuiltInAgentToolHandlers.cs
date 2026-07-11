


namespace Core.Agents.ToolHandlers;

/// <summary>
/// 内置 Agent 工具处理器 - 提供通过 MCP 调用内置 Agent 的功能
/// </summary>
[McpToolHandler(ToolCategory.Agent, Optional = true)]
[Register]
public partial class BuiltInAgentToolHandlers
{
    [Inject] private readonly IChatClient _kernel;
    [Inject] private readonly IBuiltInAgentFactory _agentFactory;
    [Inject] private readonly ILogger<BuiltInAgentToolHandlers>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 调用计划 Agent 制定任务计划
    /// </summary>
    [McpTool(AgentToolNameConstants.PlanAgent, "Use Plan Agent to create task execution plan", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> PlanAgentAsync(
        [McpToolParameter("Task goal or requirement description")] string goal,
        [McpToolParameter("Context information, optional", Required = false)] string? context = null,
        [McpToolParameter("Constraints (JSON array format), optional", Required = false)] string? constraints = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation(L.T(StringKey.PlanAgentCalledLog, goal));

            var agent = _agentFactory.CreateAgent(BuiltInAgentType.Plan);
            var planAgent = (PlanAgent)agent;

            var constraintsList = ParseStringList(constraints);

            var request = new PlanRequest
            {
                Goal = goal,
                Context = context,
                Constraints = constraintsList
            };

            var result = await planAgent.CreatePlanAsync(request, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                return ToolResultBuilder.Error()
                    .WithText(L.T(StringKey.PlanCreationFailed, result.ErrorMessage))
                    .Build();
            }

            var response = FormatPlanResult(result);
            RecordAgentToolMetrics("plan", true);
            return ToolResultBuilder.Success().WithText(response).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.PlanAgentErrorLog));
            RecordAgentToolMetrics("plan", false);
            return ToolResultBuilder.Error().WithText(L.T(StringKey.AgentCallFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 调用探索 Agent 分析代码库
    /// </summary>
    [McpTool(AgentToolNameConstants.ExploreAgent, "Use Explore Agent to analyze codebase structure", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> ExploreAgentAsync(
        [McpToolParameter("Target path or directory to explore")] string target_path,
        [McpToolParameter("Focus area, optional", Required = false)] string? focus_area = null,
        [McpToolParameter("Explore depth: overview/standard/detailed, default standard", Required = false, DefaultValue = "standard")] string depth = "standard",
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation(L.T(StringKey.ExploreAgentCalledLog, target_path));

            var agent = _agentFactory.CreateAgent(BuiltInAgentType.Explore);
            var exploreAgent = (ExploreAgent)agent;

            var exploreDepth = ParseExploreDepth(depth);

            var request = new ExploreRequest
            {
                TargetPath = target_path,
                FocusArea = focus_area,
                Depth = exploreDepth
            };

            var result = await exploreAgent.ExploreCodebaseAsync(request, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                return ToolResultBuilder.Error()
                    .WithText(L.T(StringKey.ExploreFailed, result.ErrorMessage))
                    .Build();
            }

            var response = FormatExploreResult(result);
            RecordAgentToolMetrics("explore", true);
            return ToolResultBuilder.Success().WithText(response).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ExploreAgentErrorLog));
            RecordAgentToolMetrics("explore", false);
            return ToolResultBuilder.Error().WithText(L.T(StringKey.AgentCallFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 调用验证 Agent 检查代码
    /// </summary>
    [McpTool(AgentToolNameConstants.VerificationAgent, "Use Verification Agent to check code correctness", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> VerificationAgentAsync(
        [McpToolParameter("Code content")] string code,
        [McpToolParameter("Programming language, optional", Required = false)] string? language = null,
        [McpToolParameter("Verification aspect: security/performance/maintainability/correctness/style, optional", Required = false)] string? aspect = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation(L.T(StringKey.VerificationAgentCalledLog));

            var agent = _agentFactory.CreateAgent(BuiltInAgentType.Verification);
            var verificationAgent = (VerificationAgent)agent;

            if (!string.IsNullOrWhiteSpace(aspect))
            {
                var verificationAspect = ParseVerificationAspect(aspect);
                var aspectResult = await verificationAgent.VerifyAspectAsync(code, verificationAspect, cancellationToken).ConfigureAwait(false);

                if (!aspectResult.Success)
                {
                    return ToolResultBuilder.Error()
                        .WithText(L.T(StringKey.VerificationAspectFailed, aspectResult.ErrorMessage))
                        .Build();
                }

                return ToolResultBuilder.Success()
                    .WithText(FormatVerificationResult(aspectResult))
                    .Build();
            }

            var request = new VerificationRequest
            {
                Code = code,
                Language = language
            };

            var result = await verificationAgent.VerifyCodeAsync(request, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                return ToolResultBuilder.Error()
                    .WithText(L.T(StringKey.VerificationFailed, result.ErrorMessage))
                    .Build();
            }

            var response = FormatVerificationResult(result);
            RecordAgentToolMetrics("verification", true);
            return ToolResultBuilder.Success().WithText(response).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VerificationAgentErrorLog));
            RecordAgentToolMetrics("verification", false);
            return ToolResultBuilder.Error().WithText(L.T(StringKey.AgentCallFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 调用通用 Agent 处理任务
    /// </summary>
    [McpTool(AgentToolNameConstants.GeneralAgent, "Use General Agent to handle various tasks", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> GeneralAgentAsync(
        [McpToolParameter("Task description")] string task,
        [McpToolParameter("Input content, optional", Required = false)] string? input = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation(L.T(StringKey.GeneralAgentCalledLog, task));

            var agent = _agentFactory.CreateAgent(BuiltInAgentType.GeneralPurpose);
            var generalAgent = (GeneralPurposeAgent)agent;

            var request = new GeneralTaskRequest
            {
                TaskDescription = task,
                Input = input
            };

            var result = await generalAgent.ExecuteTaskAsync(request, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                return ToolResultBuilder.Error()
                    .WithText(L.T(StringKey.GeneralTaskFailed, result.ErrorMessage))
                    .Build();
            }

            var response = FormatGeneralResult(result);
            RecordAgentToolMetrics("general", true);
            return ToolResultBuilder.Success().WithText(response).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.GeneralAgentErrorLog));
            RecordAgentToolMetrics("general", false);
            return ToolResultBuilder.Error().WithText(L.T(StringKey.AgentCallFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 调用 Claude Code 引导 Agent
    /// </summary>
    [McpTool(AgentToolNameConstants.GuideAgent, "Use Claude Code Guide Agent to get usage help", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> GuideAgentAsync(
        [McpToolParameter("Question or help needed")] string question,
        [McpToolParameter("Feature name, optional", Required = false)] string? feature = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation(L.T(StringKey.GuideAgentCalledLog, question));

            var agent = _agentFactory.CreateAgent(BuiltInAgentType.ClaudeCodeGuide);
            var guideAgent = (ClaudeCodeGuideAgent)agent;

            GuideResult result;

            if (!string.IsNullOrWhiteSpace(feature))
            {
                result = await guideAgent.IntroduceFeatureAsync(feature, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await guideAgent.AnswerUsageQuestionAsync(question, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            if (!result.Success)
            {
                return ToolResultBuilder.Error()
                    .WithText(L.T(StringKey.GuideFailed, result.ErrorMessage))
                    .Build();
            }

            var response = FormatGuideResult(result);
            RecordAgentToolMetrics("guide", true);
            return ToolResultBuilder.Success().WithText(response).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.GuideAgentErrorLog));
            RecordAgentToolMetrics("guide", false);
            return ToolResultBuilder.Error().WithText(L.T(StringKey.AgentCallFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 列出所有可用的内置 Agent
    /// </summary>
    [McpTool(AgentToolNameConstants.ListAgents, "List all available built-in agents", AgentToolNameConstants.Agent)]
    public Task<ToolResult> ListAgentsAsync(CancellationToken cancellationToken = default)
    {
        var agentTypes = _agentFactory.GetAvailableAgentTypes();
        var response = new System.Text.StringBuilder();

        response.AppendLine(L.T(StringKey.AvailableBuiltInAgentsTitle));
        response.AppendLine();

        foreach (var agentType in agentTypes)
        {
            var config = _agentFactory.GetAgentConfig(agentType);
            if (config != null)
            {
                response.AppendLine($"## {config.Name}");
                response.AppendLine(L.T(StringKey.SyncLabelType, agentType));
                response.AppendLine(L.T(StringKey.SyncLabelDescription, config.Description));
                response.AppendLine();
            }
        }

        response.AppendLine(L.T(StringKey.UsageInstructions));
        response.AppendLine(L.T(StringKey.PlanAgentUsage));
        response.AppendLine(L.T(StringKey.ExploreAgentUsage));
        response.AppendLine(L.T(StringKey.VerificationAgentUsage));
        response.AppendLine(L.T(StringKey.GeneralAgentUsage));
        response.AppendLine(L.T(StringKey.GuideAgentUsage));

        return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
    }

    #region Private Methods

    private void RecordAgentToolMetrics(string agentType, bool isSuccess)
        => _telemetryService?.RecordCount("agent.tool.invoked.count", new Dictionary<string, string> { ["agent"] = agentType, ["success"] = isSuccess.ToString() }, "count", "Agent tool invoked count");

    private static List<string>? ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize(json, AgentsJsonContext.Default.ListString);
        }
        catch
        {
            return null;
        }
    }

    private static ExploreDepth ParseExploreDepth(string depth) => depth.ToLowerInvariant() switch
    {
        "overview" => ExploreDepth.Overview,
        "detailed" => ExploreDepth.Detailed,
        _ => ExploreDepth.Standard
    };

    private static VerificationAspect ParseVerificationAspect(string aspect) => aspect.ToLowerInvariant() switch
    {
        "security" => VerificationAspect.Security,
        "performance" => VerificationAspect.Performance,
        "maintainability" => VerificationAspect.Maintainability,
        "correctness" => VerificationAspect.Correctness,
        "style" => VerificationAspect.Style,
        _ => VerificationAspect.Correctness
    };

    private static string FormatPlanResult(PlanResult result)
    {
        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.PlanGenerated, result.PlanId));
        response.AppendLine(L.T(StringKey.LabelDurationMs, result.ExecutionTimeMs));
        response.AppendLine(L.T(StringKey.LabelTokenUsage, result.TokenUsage.PromptTokens, result.TokenUsage.CompletionTokens));
        response.AppendLine();
        response.AppendLine(result.Content);
        return response.ToString();
    }

    private static string FormatExploreResult(ExploreResult result)
    {
        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.ExploreCompleted, result.ExploreId));
        response.AppendLine(L.T(StringKey.LabelDurationMs, result.ExecutionTimeMs));
        response.AppendLine(L.T(StringKey.LabelTokenUsage, result.TokenUsage.PromptTokens, result.TokenUsage.CompletionTokens));
        response.AppendLine();
        response.AppendLine(result.Content);
        return response.ToString();
    }

    private static string FormatVerificationResult(VerificationResult result)
    {
        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.VerificationCompleted, result.VerificationId));
        response.AppendLine(L.T(StringKey.LabelDurationMs, result.ExecutionTimeMs));
        response.AppendLine(L.T(StringKey.LabelTokenUsage, result.TokenUsage.PromptTokens, result.TokenUsage.CompletionTokens));
        response.AppendLine();
        response.AppendLine(result.Content);
        return response.ToString();
    }

    private static string FormatGeneralResult(GeneralTaskResult result)
    {
        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.TaskCompleted, result.TaskId));
        response.AppendLine(L.T(StringKey.LabelDurationMs, result.ExecutionTimeMs));
        response.AppendLine(L.T(StringKey.LabelTokenUsage, result.TokenUsage.PromptTokens, result.TokenUsage.CompletionTokens));
        response.AppendLine();
        response.AppendLine(result.Content);
        return response.ToString();
    }

    private static string FormatGuideResult(GuideResult result)
    {
        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.HelpInfo, result.GuideId));
        response.AppendLine(L.T(StringKey.LabelDurationMs, result.ExecutionTimeMs));
        response.AppendLine(L.T(StringKey.LabelTokenUsage, result.TokenUsage.PromptTokens, result.TokenUsage.CompletionTokens));
        response.AppendLine();
        response.AppendLine(result.Content);
        return response.ToString();
    }

    #endregion
}
