namespace Core.Agents.ToolHandlers;

[McpToolDispatch(ToolCategory.Agent, Optional = true)]
[Register]
public partial class BuiltInAgentToolHandlers
{
    [Inject] private readonly IAgentService _agentService;
    [Inject] private readonly IAgentRoleRegistry _roleRegistry;
    [Inject] private readonly ILogger<BuiltInAgentToolHandlers>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;

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

            var prompt = BuildPlanPrompt(goal, context, constraints);
            var options = new AgentSpawnOptions
            {
                Description = $"Plan: {goal}",
                Prompt = prompt,
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Plan,
            };

            var agentInfo = await _agentService.SpawnAgentAsync(options, cancellationToken).ConfigureAwait(false);
            var result = await _agentService.WaitForAgentAsync(agentInfo.Id, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                return ToolResultBuilder.Error()
                    .WithText(L.T(StringKey.PlanCreationFailed, result.Error))
                    .Build();
            }

            RecordAgentToolMetrics("plan", true);
            return ToolResultBuilder.Success().WithText(result.Output).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.PlanAgentErrorLog));
            RecordAgentToolMetrics("plan", false);
            return ToolResultBuilder.Error().WithText(L.T(StringKey.AgentCallFailed, ex.Message)).Build();
        }
    }

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

            var prompt = BuildExplorePrompt(target_path, focus_area, depth);
            var options = new AgentSpawnOptions
            {
                Description = $"Explore: {target_path}",
                Prompt = prompt,
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Explore,
            };

            var agentInfo = await _agentService.SpawnAgentAsync(options, cancellationToken).ConfigureAwait(false);
            var result = await _agentService.WaitForAgentAsync(agentInfo.Id, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                return ToolResultBuilder.Error()
                    .WithText(L.T(StringKey.ExploreFailed, result.Error))
                    .Build();
            }

            RecordAgentToolMetrics("explore", true);
            return ToolResultBuilder.Success().WithText(result.Output).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ExploreAgentErrorLog));
            RecordAgentToolMetrics("explore", false);
            return ToolResultBuilder.Error().WithText(L.T(StringKey.AgentCallFailed, ex.Message)).Build();
        }
    }

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

            var prompt = BuildVerificationPrompt(code, language, aspect);
            var options = new AgentSpawnOptions
            {
                Description = "Verification task",
                Prompt = prompt,
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Verification,
            };

            var agentInfo = await _agentService.SpawnAgentAsync(options, cancellationToken).ConfigureAwait(false);
            var result = await _agentService.WaitForAgentAsync(agentInfo.Id, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                return ToolResultBuilder.Error()
                    .WithText(L.T(StringKey.VerificationFailed, result.Error))
                    .Build();
            }

            RecordAgentToolMetrics("verification", true);
            return ToolResultBuilder.Success().WithText(result.Output).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VerificationAgentErrorLog));
            RecordAgentToolMetrics("verification", false);
            return ToolResultBuilder.Error().WithText(L.T(StringKey.AgentCallFailed, ex.Message)).Build();
        }
    }

    [McpTool(AgentToolNameConstants.GeneralAgent, "Use General Agent to handle various tasks", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> GeneralAgentAsync(
        [McpToolParameter("Task description")] string task,
        [McpToolParameter("Input content, optional", Required = false)] string? input = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation(L.T(StringKey.GeneralAgentCalledLog, task));

            var prompt = BuildGeneralPrompt(task, input);
            var options = new AgentSpawnOptions
            {
                Description = $"General: {task}",
                Prompt = prompt,
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Code,
            };

            var agentInfo = await _agentService.SpawnAgentAsync(options, cancellationToken).ConfigureAwait(false);
            var result = await _agentService.WaitForAgentAsync(agentInfo.Id, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                return ToolResultBuilder.Error()
                    .WithText(L.T(StringKey.GeneralTaskFailed, result.Error))
                    .Build();
            }

            RecordAgentToolMetrics("general", true);
            return ToolResultBuilder.Success().WithText(result.Output).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.GeneralAgentErrorLog));
            RecordAgentToolMetrics("general", false);
            return ToolResultBuilder.Error().WithText(L.T(StringKey.AgentCallFailed, ex.Message)).Build();
        }
    }

    [McpTool(AgentToolNameConstants.GuideAgent, "Use Claude Code Guide Agent to get usage help", AgentToolNameConstants.Agent)]
    public async Task<ToolResult> GuideAgentAsync(
        [McpToolParameter("Question or help needed")] string question,
        [McpToolParameter("Feature name, optional", Required = false)] string? feature = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation(L.T(StringKey.GuideAgentCalledLog, question));

            var prompt = BuildGuidePrompt(question, feature);
            var options = new AgentSpawnOptions
            {
                Description = $"Guide: {question}",
                Prompt = prompt,
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.ClaudeCodeGuide,
            };

            var agentInfo = await _agentService.SpawnAgentAsync(options, cancellationToken).ConfigureAwait(false);
            var result = await _agentService.WaitForAgentAsync(agentInfo.Id, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                return ToolResultBuilder.Error()
                    .WithText(L.T(StringKey.GuideFailed, result.Error))
                    .Build();
            }

            RecordAgentToolMetrics("guide", true);
            return ToolResultBuilder.Success().WithText(result.Output).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.GuideAgentErrorLog));
            RecordAgentToolMetrics("guide", false);
            return ToolResultBuilder.Error().WithText(L.T(StringKey.AgentCallFailed, ex.Message)).Build();
        }
    }

    [McpTool(AgentToolNameConstants.ListAgents, "List all available built-in agents", AgentToolNameConstants.Agent)]
    public Task<ToolResult> ListAgentsAsync(CancellationToken cancellationToken = default)
    {
        var profiles = _roleRegistry.GetAllProfiles();
        var response = new System.Text.StringBuilder();

        response.AppendLine(L.T(StringKey.AvailableBuiltInAgentsTitle));
        response.AppendLine();

        foreach (var profile in profiles)
        {
            var label = profile.Variant.HasValue
                ? $"{profile.Role.ToValue()}:{profile.Variant.Value.ToValue()}"
                : profile.Role.ToValue();

            response.AppendLine($"## {label}");
            response.AppendLine(L.T(StringKey.SyncLabelDescription, profile.Description));
            response.AppendLine();
        }

        return Task.FromResult(ToolResultBuilder.Success().WithText(response.ToString()).Build());
    }

    #region Private Methods

    private void RecordAgentToolMetrics(string agentType, bool isSuccess)
        => _telemetryService?.RecordCount("agent.tool.invoked.count", new Dictionary<string, string> { ["agent"] = agentType, ["success"] = isSuccess.ToString() }, "count", "Agent tool invoked count");

    private static string BuildPlanPrompt(string goal, string? context, string? constraints)
    {
        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine("请为以下目标制定详细的执行计划：");
        prompt.AppendLine();
        prompt.AppendLine($"## 目标\n{goal}");

        if (!string.IsNullOrWhiteSpace(context))
        {
            prompt.AppendLine();
            prompt.AppendLine($"## 上下文\n{context}");
        }

        if (!string.IsNullOrWhiteSpace(constraints))
        {
            prompt.AppendLine();
            prompt.AppendLine($"## 约束条件\n{constraints}");
        }

        prompt.AppendLine();
        prompt.AppendLine("请按照系统提示词中指定的格式输出计划。");
        return prompt.ToString();
    }

    private static string BuildExplorePrompt(string targetPath, string? focusArea, string depth)
    {
        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine("请探索以下路径的代码库结构：");
        prompt.AppendLine();
        prompt.AppendLine($"## 目标路径\n{targetPath}");
        prompt.AppendLine($"## 探索深度\n{depth}");

        if (!string.IsNullOrWhiteSpace(focusArea))
        {
            prompt.AppendLine();
            prompt.AppendLine($"## 关注领域\n{focusArea}");
        }

        return prompt.ToString();
    }

    private static string BuildVerificationPrompt(string code, string? language, string? aspect)
    {
        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine("请验证以下代码：");
        prompt.AppendLine();
        prompt.AppendLine("## 代码");
        prompt.AppendLine("```");
        prompt.AppendLine(code);
        prompt.AppendLine("```");

        if (!string.IsNullOrWhiteSpace(language))
        {
            prompt.AppendLine();
            prompt.AppendLine($"## 编程语言\n{language}");
        }

        if (!string.IsNullOrWhiteSpace(aspect))
        {
            prompt.AppendLine();
            prompt.AppendLine($"## 重点验证方面\n{aspect}");
        }

        return prompt.ToString();
    }

    private static string BuildGeneralPrompt(string task, string? input)
    {
        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine("请执行以下任务：");
        prompt.AppendLine();
        prompt.AppendLine($"## 任务描述\n{task}");

        if (!string.IsNullOrWhiteSpace(input))
        {
            prompt.AppendLine();
            prompt.AppendLine($"## 输入内容\n{input}");
        }

        return prompt.ToString();
    }

    private static string BuildGuidePrompt(string question, string? feature)
    {
        if (!string.IsNullOrWhiteSpace(feature))
        {
            return $"请详细介绍 Claude Code 的以下功能：\n\n## 功能名称\n{feature}\n\n请提供功能概述、使用场景、详细步骤和实际示例。";
        }

        return $"请回答以下关于 Claude Code 使用的问题：\n\n## 问题\n{question}\n\n请提供直接回答、相关背景和具体示例。";
    }

    #endregion
}
