namespace Core.Skills;

/// <summary>
/// 技能步骤执行中间件 — 执行技能步骤（Tool/Prompt/Loop）
/// </summary>
[Register]
public sealed partial class SkillExecutionMiddleware : ISkillMiddleware
{
    private readonly IQueryEngine _queryEngine;
    private readonly IToolRegistry _toolRegistry;
    private readonly IVariableResolver _variableResolver;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <summary>
    /// 创建 SkillExecutionMiddleware
    /// </summary>
    public SkillExecutionMiddleware(IQueryEngine queryEngine, IToolRegistry toolRegistry, IVariableResolver variableResolver)
    {
        _queryEngine = queryEngine;
        _toolRegistry = toolRegistry;
        _variableResolver = variableResolver;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(SkillContext context, MiddlewareDelegate<SkillContext> next, CancellationToken ct)
    {
        var skill = context.Skill!;
        var parameters = context.Parameters ?? new Dictionary<string, JsonElement>();
        var result = await ExecuteSkillStepsAsync(skill, parameters, context).ConfigureAwait(false);
        context.Result = result;

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task<SkillResult> ExecuteSkillStepsAsync(
        SkillDefinition skill,
        Dictionary<string, JsonElement> parameters,
        SkillContext context)
    {
        var variables = new Dictionary<string, JsonElement>(parameters, StringComparer.OrdinalIgnoreCase);
        var stepResults = new List<string>();
        var currentStepId = skill.Steps.FirstOrDefault()?.Id;
        var executedSteps = new HashSet<string>();

        while (currentStepId != null && !context.CancellationToken.IsCancellationRequested)
        {
            if (executedSteps.Contains(currentStepId))
            {
                break;
            }

            executedSteps.Add(currentStepId);
            var step = skill.Steps.FirstOrDefault(s => s.Id == currentStepId);
            if (step == null)
            {
                break;
            }

            var stepResult = await ExecuteStepAsync(step, variables, context).ConfigureAwait(false);
            if (!stepResult.IsSuccess)
            {
                if (step.OnError != null)
                {
                    currentStepId = step.OnError;
                    continue;
                }
                return SkillResult.FailureResult(skill.Name, stepResult.Error ?? ContractsErrorMessages.StepExecutionFailed);
            }

            if (!string.IsNullOrEmpty(stepResult.Output))
            {
                stepResults.Add(stepResult.Output);
            }

            currentStepId = step.Next;
        }

        return SkillResult.SuccessResult(skill.Name, string.Join("\n", stepResults));
    }

    private async Task<StepExecutionResult> ExecuteStepAsync(
        SkillStep step,
        Dictionary<string, JsonElement> variables,
        SkillContext context)
    {
        try
        {
            string? output = step.Type switch
            {
                SkillStepType.Tool => await ExecuteToolStepAsync(step, variables, context).ConfigureAwait(false),
                SkillStepType.Prompt => await ExecutePromptStepAsync(step, variables, context).ConfigureAwait(false),
                SkillStepType.Loop => await ExecuteLoopStepAsync(step, variables, context).ConfigureAwait(false),
                _ => throw new NotSupportedException(L.T(StringKey.SkillServiceUnsupportedStepType, step.Type.ToValue()))
            };

            return new StepExecutionResult { IsSuccess = true, Output = output };
        }
        catch (Exception ex)
        {
            return new StepExecutionResult { IsSuccess = false, Error = ex.Message };
        }
    }

    private async Task<string?> ExecuteToolStepAsync(
        SkillStep step,
        Dictionary<string, JsonElement> variables,
        SkillContext context)
    {
        if (string.IsNullOrEmpty(step.Tool))
        {
            throw new InvalidOperationException(ContractsErrorMessages.ToolStepMustSpecifyTool);
        }

        var prompt = step.Prompt != null ? _variableResolver.Resolve(step.Prompt, variables) : "";
        var arguments = new Dictionary<string, JsonElement>
        {
            ["input"] = JsonSerializer.SerializeToElement(prompt, SkillsJsonContext.Default.String)
        };

        var result = await _toolRegistry.ExecuteToolAsync(step.Tool, arguments, context.CancellationToken).ConfigureAwait(false);

        if (result.IsError)
        {
            var errorContent = result.Content?.FirstOrDefault(c => !string.IsNullOrEmpty(c.Text))?.Text ?? L.T(StringKey.SkillServiceUnknownError);
            throw new InvalidOperationException(L.T(StringKey.SkillServiceToolExecutionFailed, step.Tool, errorContent));
        }

        return result.Content?.FirstOrDefault(c => !string.IsNullOrEmpty(c.Text))?.Text ?? "";
    }

    private async Task<string?> ExecutePromptStepAsync(
        SkillStep step,
        Dictionary<string, JsonElement> variables,
        SkillContext context)
    {
        if (string.IsNullOrEmpty(step.Prompt))
        {
            throw new InvalidOperationException(ContractsErrorMessages.PromptStepMustSpecifyPrompt);
        }

        var prompt = _variableResolver.Resolve(step.Prompt, variables);
        var chatHistory = new JoinCode.Abstractions.LLM.Chat.MessageList();
        chatHistory.AddUserMessage(prompt);

        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var chunk in _queryEngine.QueryAsync(prompt, chatHistory, context.CancellationToken))
        {
            if (chunk.Type == AgentStreamChunkType.Content)
            {
                responseBuilder.Append(chunk.Content);
            }
        }

        return responseBuilder.ToString();
    }

    private async Task<string?> ExecuteLoopStepAsync(
        SkillStep step,
        Dictionary<string, JsonElement> variables,
        SkillContext context)
    {
        if (step.Loop == null)
        {
            throw new InvalidOperationException(ContractsErrorMessages.LoopStepMustSpecifyLoopConfig);
        }

        var results = new List<string>();
        var maxIterations = step.Loop.Count ?? 10;

        for (var i = 0; i < maxIterations && !context.CancellationToken.IsCancellationRequested; i++)
        {
            variables["iteration"] = JsonSerializer.SerializeToElement(i + 1, SkillsJsonContext.Default.Int32);

            if (!string.IsNullOrEmpty(step.Prompt))
            {
                var result = await ExecutePromptStepAsync(step, variables, context).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(result))
                {
                    results.Add(result);
                }
            }

            if (step.Loop.Condition != null)
            {
                var condition = _variableResolver.Resolve(step.Loop.Condition, variables);
                if (condition.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                    condition.Equals("0", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }

        return string.Join("\n", results);
    }

    private sealed class StepExecutionResult
    {
        public bool IsSuccess { get; init; }
        public string? Output { get; init; }
        public string? Error { get; init; }
    }
}
