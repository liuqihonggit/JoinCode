namespace Core.Agents.ToolHandlers;

[McpToolDispatch(ToolCategory.Agent, Optional = true)]
[Register]
public partial class BuiltInAgentToolHandlers : ServiceEntity
{

    public BuiltInAgentToolHandlers(
        IAgentService agentService,
        IAgentRoleRegistry roleRegistry,
        ILogger<BuiltInAgentToolHandlers>? logger = null,
        ITelemetryService? telemetryService = null,
        SubAgentOutputTruncator? outputTruncator = null,
        SubAgentSummaryGenerator? summaryGenerator = null,
        SubAgentConfig? subAgentConfig = null,
        IChatContextManager? contextManager = null,
        JoinCode.Abstractions.Interfaces.IAgentPromptBuilder? promptBuilder = null)
    {
        _agentService = agentService;
        _roleRegistry = roleRegistry;
        _logger = logger;
        _telemetryService = telemetryService;
        _outputTruncator = outputTruncator;
        _summaryGenerator = summaryGenerator;
        _subAgentConfig = subAgentConfig;
        _contextManager = contextManager;
        _promptBuilder = promptBuilder;
    }
    private readonly IAgentService _agentService;
    private readonly IAgentRoleRegistry _roleRegistry;
    private readonly ILogger<BuiltInAgentToolHandlers>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly SubAgentOutputTruncator? _outputTruncator;
    private readonly SubAgentSummaryGenerator? _summaryGenerator;
    private readonly SubAgentConfig? _subAgentConfig;
    private readonly IChatContextManager? _contextManager;
    private readonly JoinCode.Abstractions.Interfaces.IAgentPromptBuilder? _promptBuilder;

    private const int DefaultOutputTokenBudget = 50_000;

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
                var diag = BuildPlanCreationFailedDiagnostic(result.Error);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }

            RecordAgentToolMetrics("plan", true);
            return ToolResultBuilder.Success().WithText(await BuildAgentOutputAsync(agentInfo.Id, result.Output, cancellationToken)).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.PlanAgentErrorLog));
            RecordAgentToolMetrics("plan", false);
            var planExDiag = BuildPlanAgentExceptionDiagnostic(ex.Message);
            return ToolResultBuilder.Error().WithText(planExDiag.FormattedMessage).WithDiagnostic(planExDiag).Build();
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
                var diag = BuildExploreFailedDiagnostic(result.Error);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }

            RecordAgentToolMetrics("explore", true);
            return ToolResultBuilder.Success().WithText(await BuildAgentOutputAsync(agentInfo.Id, result.Output, cancellationToken)).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ExploreAgentErrorLog));
            RecordAgentToolMetrics("explore", false);
            var exploreExDiag = BuildExploreAgentExceptionDiagnostic(ex.Message);
            return ToolResultBuilder.Error().WithText(exploreExDiag.FormattedMessage).WithDiagnostic(exploreExDiag).Build();
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
                var diag = BuildVerificationFailedDiagnostic(result.Error);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }

            RecordAgentToolMetrics("verification", true);
            return ToolResultBuilder.Success().WithText(await BuildAgentOutputAsync(agentInfo.Id, result.Output, cancellationToken)).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VerificationAgentErrorLog));
            RecordAgentToolMetrics("verification", false);
            var verificationExDiag = BuildVerificationAgentExceptionDiagnostic(ex.Message);
            return ToolResultBuilder.Error().WithText(verificationExDiag.FormattedMessage).WithDiagnostic(verificationExDiag).Build();
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
                var diag = BuildGeneralTaskFailedDiagnostic(result.Error);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }

            RecordAgentToolMetrics("general", true);
            return ToolResultBuilder.Success().WithText(await BuildAgentOutputAsync(agentInfo.Id, result.Output, cancellationToken)).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.GeneralAgentErrorLog));
            RecordAgentToolMetrics("general", false);
            var generalExDiag = BuildGeneralAgentExceptionDiagnostic(ex.Message);
            return ToolResultBuilder.Error().WithText(generalExDiag.FormattedMessage).WithDiagnostic(generalExDiag).Build();
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
            var systemPrompt = _promptBuilder is not null
                ? await _promptBuilder.BuildSystemPromptAsync(
                    ExecutorVariant.ClaudeCodeGuide.ToValue(),
                    question,
                    null,
                    BuildGuidePromptContext(cancellationToken),
                    cancellationToken).ConfigureAwait(false)
                : null;
            var options = new AgentSpawnOptions
            {
                Description = $"Guide: {question}",
                Prompt = prompt,
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.ClaudeCodeGuide,
                SystemPrompt = systemPrompt,
            };

            var agentInfo = await _agentService.SpawnAgentAsync(options, cancellationToken).ConfigureAwait(false);
            var result = await _agentService.WaitForAgentAsync(agentInfo.Id, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                var diag = BuildGuideFailedDiagnostic(result.Error);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }

            RecordAgentToolMetrics("guide", true);
            return ToolResultBuilder.Success().WithText(await BuildAgentOutputAsync(agentInfo.Id, result.Output, cancellationToken)).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.GuideAgentErrorLog));
            RecordAgentToolMetrics("guide", false);
            var guideExDiag = BuildGuideAgentExceptionDiagnostic(ex.Message);
            return ToolResultBuilder.Error().WithText(guideExDiag.FormattedMessage).WithDiagnostic(guideExDiag).Build();
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

    /// <summary>
    /// 构建 GuideAgent 运行时上下文 — 注入当前可用 agent 列表
    /// <para>对齐 claude code claude-code-guide agent 的 getSystemPrompt({ toolUseContext }) 闭包模式</para>
    /// </summary>
    private AgentPromptContext BuildGuidePromptContext(CancellationToken cancellationToken)
    {
        var agentTypes = _roleRegistry.GetAllProfiles()
            .Select(p => p.Variant.HasValue
                ? $"{p.Role.ToValue()}:{p.Variant.Value.ToValue()}"
                : p.Role.ToValue())
            .ToList();

        return new AgentPromptContext
        {
            AvailableSkills = agentTypes,
            SettingsSummary = "Guide 模式 — 已注入可用 agent 列表",
        };
    }

    /// <summary>
    /// 构建子智能体输出文本 — L0 XML 包装 + L1 直接放 + L2 自摘要 + L3 落盘指针
    /// <para>步3: L0+L3 固定阈值。步4: 接入 L2 自摘要。后续4: 动态预算 R = min(ctxMax/4, fallback)。</para>
    /// </summary>
    private async Task<string> BuildAgentOutputAsync(string agentId, string output, CancellationToken cancellationToken)
    {
        if (_outputTruncator is null)
            return output;

        var budget = CalculateOutputTokenBudget();
        var summary = SubAgentOutputEnvelope.ExtractSummary(output);

        if (_summaryGenerator is not null)
        {
            var summaryResult = await _summaryGenerator.TrySummarizeAsync(agentId, output, budget, cancellationToken).ConfigureAwait(false);
            if (summaryResult.Status == SubAgentSummaryStatus.Success)
                return SubAgentOutputEnvelope.Wrap(agentId, SubAgentEnvelopeState.Completed, summary, summaryResult.Summary!);
            if (summaryResult.Status == SubAgentSummaryStatus.NotNeeded)
                return SubAgentOutputEnvelope.Wrap(agentId, SubAgentEnvelopeState.Completed, summary, output);
        }

        var truncation = await _outputTruncator.TruncateAsync(agentId, output, budget, summary, cancellationToken).ConfigureAwait(false);
        return SubAgentOutputEnvelope.Wrap(agentId, SubAgentEnvelopeState.Completed, summary, truncation.FinalText);
    }

    /// <summary>
    /// 计算子智能体输出 token 预算 — 动态预算 R = min(ctxMax/4, fallback)
    /// <para>IChatContextManager 可用时用 ctxMax/4（1/4 窗口），否则用固定回退值。</para>
    /// </summary>
    private int CalculateOutputTokenBudget()
    {
        var fallback = _subAgentConfig?.FallbackOutputTokenBudget ?? DefaultOutputTokenBudget;
        if (_contextManager is null)
            return fallback;

        var ctxMax = _contextManager.GetContextMaxTokens();
        if (ctxMax <= 0)
            return fallback;

        return Math.Min(ctxMax / 4, fallback);
    }

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

    #region Diagnostic Builders

    /// <summary>
    /// Plan Agent 创建计划失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildPlanCreationFailedDiagnostic(string? error)
    {
        return ToolDiagnostic.Create("PlanCreationFailed", L.T(StringKey.PlanCreationFailed, error),
            [new DiagnosticDetail("error", error ?? string.Empty)],
            ["检查计划目标是否清晰，约束条件是否合理，必要时简化目标后重试。"]);
    }

    /// <summary>
    /// Plan Agent 执行抛出异常的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildPlanAgentExceptionDiagnostic(string errorMessage)
    {
        return ToolDiagnostic.Create("PlanAgentException", L.T(StringKey.AgentCallFailed, errorMessage),
            [new DiagnosticDetail("exception", errorMessage)],
            ["查看日志获取完整异常堆栈，确认 AgentService 可用后重试计划生成。"]);
    }

    /// <summary>
    /// Explore Agent 探索失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildExploreFailedDiagnostic(string? error)
    {
        return ToolDiagnostic.Create("ExploreFailed", L.T(StringKey.ExploreFailed, error),
            [new DiagnosticDetail("error", error ?? string.Empty)],
            ["确认目标路径存在且可访问，调整探索深度或关注领域后重试。"]);
    }

    /// <summary>
    /// Explore Agent 执行抛出异常的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildExploreAgentExceptionDiagnostic(string errorMessage)
    {
        return ToolDiagnostic.Create("ExploreAgentException", L.T(StringKey.AgentCallFailed, errorMessage),
            [new DiagnosticDetail("exception", errorMessage)],
            ["查看日志获取完整异常堆栈，确认 AgentService 可用后重试代码库探索。"]);
    }

    /// <summary>
    /// Verification Agent 验证失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildVerificationFailedDiagnostic(string? error)
    {
        return ToolDiagnostic.Create("VerificationFailed", L.T(StringKey.VerificationFailed, error),
            [new DiagnosticDetail("error", error ?? string.Empty)],
            ["检查代码语法是否正确，确认验证方面适用，必要时调整 language 或 aspect 参数。"]);
    }

    /// <summary>
    /// Verification Agent 执行抛出异常的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildVerificationAgentExceptionDiagnostic(string errorMessage)
    {
        return ToolDiagnostic.Create("VerificationAgentException", L.T(StringKey.AgentCallFailed, errorMessage),
            [new DiagnosticDetail("exception", errorMessage)],
            ["查看日志获取完整异常堆栈，确认 AgentService 可用后重试代码验证。"]);
    }

    /// <summary>
    /// General Agent 任务失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildGeneralTaskFailedDiagnostic(string? error)
    {
        return ToolDiagnostic.Create("GeneralTaskFailed", L.T(StringKey.GeneralTaskFailed, error),
            [new DiagnosticDetail("error", error ?? string.Empty)],
            ["检查任务描述是否清晰，输入内容是否完整，必要时补充上下文后重试。"]);
    }

    /// <summary>
    /// General Agent 执行抛出异常的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildGeneralAgentExceptionDiagnostic(string errorMessage)
    {
        return ToolDiagnostic.Create("GeneralAgentException", L.T(StringKey.AgentCallFailed, errorMessage),
            [new DiagnosticDetail("exception", errorMessage)],
            ["查看日志获取完整异常堆栈，确认 AgentService 可用后重试通用任务。"]);
    }

    /// <summary>
    /// Guide Agent 获取帮助失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildGuideFailedDiagnostic(string? error)
    {
        return ToolDiagnostic.Create("GuideFailed", L.T(StringKey.GuideFailed, error),
            [new DiagnosticDetail("error", error ?? string.Empty)],
            ["确认问题表述清晰，可尝试提供更具体的 feature 参数后重试。"]);
    }

    /// <summary>
    /// Guide Agent 执行抛出异常的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildGuideAgentExceptionDiagnostic(string errorMessage)
    {
        return ToolDiagnostic.Create("GuideAgentException", L.T(StringKey.AgentCallFailed, errorMessage),
            [new DiagnosticDetail("exception", errorMessage)],
            ["查看日志获取完整异常堆栈，确认 AgentService 可用后重试获取使用帮助。"]);
    }

    #endregion
}
