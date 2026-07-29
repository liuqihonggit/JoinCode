
namespace Core.Skills;

/// <summary>
/// 技能执行器
/// </summary>
public sealed partial class SkillExecutor
{
    private readonly IQueryEngine _queryEngine;
    private readonly IToolRegistry _toolRegistry;
    [Inject] private readonly ILogger<SkillExecutor>? _logger;
    private readonly Dictionary<string, JsonElement> _variables;
    private readonly IVariableResolver _variableResolver;

    /// <summary>
    /// 初始化技能执行器
    /// </summary>
    public SkillExecutor(IQueryEngine queryEngine, IToolRegistry toolRegistry, ILogger<SkillExecutor>? logger = null, IVariableResolver? variableResolver = null)
    {
        _queryEngine = queryEngine ?? throw new ArgumentNullException(nameof(queryEngine));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _logger = logger;
        _variables = new Dictionary<string, JsonElement>();
        _variableResolver = variableResolver ?? new VariableResolver();
    }

    /// <summary>
    /// 执行技能
    /// </summary>
    public async Task<SkillExecutionResult> ExecuteAsync(
        SkillDefinition skill,
        Dictionary<string, JsonElement> parameters,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var stepResults = new List<StepResult>();

        _logger?.LogInformation(L.T(StringKey.SkillExecutorStartExecution), skill.Name);

        var validationError = ValidateParameters(skill, parameters);
        if (validationError != null)
        {
            return new SkillExecutionResult
            {
                SkillName = skill.Name,
                IsSuccess = false,
                Output = string.Empty,
                Error = validationError,
                StepResults = stepResults,
                ExecutionTime = stopwatch.Elapsed
            };
        }

        foreach (var param in parameters)
        {
            _variables[param.Key] = param.Value;
            _variables[$"{{{{{param.Key}}}}}"] = param.Value;
        }

        try
        {
            var currentStepId = skill.Steps.FirstOrDefault()?.Id;
            var executedSteps = new HashSet<string>();

            while (currentStepId != null && !cancellationToken.IsCancellationRequested)
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

                var stepResult = await ExecuteStepAsync(step, cancellationToken).ConfigureAwait(false);
                stepResults.Add(stepResult);

                if (!stepResult.IsSuccess)
                {
                    if (step.OnError != null)
                    {
                        currentStepId = step.OnError;
                        continue;
                    }

                    stopwatch.Stop();
                    return new SkillExecutionResult
                    {
                        SkillName = skill.Name,
                        IsSuccess = false,
                        Output = string.Join("\n", stepResults.Where(r => r.Output != null).Select(r => r.Output)),
                        Error = stepResult.Error,
                        StepResults = stepResults,
                        ExecutionTime = stopwatch.Elapsed
                    };
                }

                currentStepId = step.Next;
            }

            stopwatch.Stop();

            var finalOutput = string.Join("\n", stepResults
                .Where(r => r.Output != null)
                .Select(r => r.Output));

            _logger?.LogInformation(L.T(StringKey.SkillExecutorExecutionComplete), skill.Name);

            return new SkillExecutionResult
            {
                SkillName = skill.Name,
                IsSuccess = true,
                Output = finalOutput,
                StepResults = stepResults,
                ExecutionTime = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger?.LogWarning(L.T(StringKey.SkillExecutorExecutionCancelled), skill.Name);

            return new SkillExecutionResult
            {
                SkillName = skill.Name,
                IsSuccess = false,
                Output = string.Empty,
                Error = L.T(StringKey.SkillExecutorExecutionCancelledResult),
                StepResults = stepResults,
                ExecutionTime = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, L.T(StringKey.SkillExecutorExecutionFailed), skill.Name);

            return new SkillExecutionResult
            {
                SkillName = skill.Name,
                IsSuccess = false,
                Output = string.Empty,
                Error = ex.Message,
                StepResults = stepResults,
                ExecutionTime = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// 执行单个步骤
    /// </summary>
    private async Task<StepResult> ExecuteStepAsync(SkillStep step, CancellationToken cancellationToken)
    {
        var stepStopwatch = Stopwatch.StartNew();
        _logger?.LogInformation("[SkillExecutor] 执行步骤: {StepId} ({StepType})", step.Id, step.Type);

        try
        {
            string? output = null;

            switch (step.Type)
            {
                case SkillStepType.Tool:
                    output = await ExecuteToolStepAsync(step, cancellationToken).ConfigureAwait(false);
                    break;

                case SkillStepType.Prompt:
                    output = await ExecutePromptStepAsync(step, cancellationToken).ConfigureAwait(false);
                    break;

                case SkillStepType.Loop:
                    output = await ExecuteLoopStepAsync(step, cancellationToken).ConfigureAwait(false);
                    break;

                case SkillStepType.Condition:
                    output = await ExecuteConditionStepAsync(step, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    throw new NotSupportedException(L.T(StringKey.SkillExecutorUnsupportedStepType, step.Type.ToValue()));
            }

            stepStopwatch.Stop();

            return new StepResult
            {
                StepId = step.Id,
                IsSuccess = true,
                Output = output,
                ExecutionTime = stepStopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stepStopwatch.Stop();

            return new StepResult
            {
                StepId = step.Id,
                IsSuccess = false,
                Error = ex.Message,
                ExecutionTime = stepStopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// 执行工具步骤
    /// </summary>
    private async Task<string> ExecuteToolStepAsync(SkillStep step, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(step.Tool))
        {
            throw new InvalidOperationException(ContractsErrorMessages.ToolStepMustSpecifyTool);
        }

        var toolName = step.Tool;
        _logger?.LogInformation("[SkillExecutor] 调用工具: {Tool}", toolName);

        try
        {
            var arguments = ParseToolArguments(step);

            var result = await _toolRegistry.ExecuteToolAsync(toolName, arguments, cancellationToken).ConfigureAwait(false);

            if (result.IsError)
            {
                var errorContent = ExtractTextFromResult(result);
                throw new InvalidOperationException(L.T(StringKey.SkillExecutorToolExecutionFailed, toolName, errorContent));
            }

            return ExtractTextFromResult(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger?.LogError(ex, "[SkillExecutor] 工具 {Tool} 执行失败", toolName);
            throw new InvalidOperationException(L.T(StringKey.SkillExecutorToolExecutionFailed, toolName, ex.Message), ex);
        }
    }

    /// <summary>
    /// 解析工具参数
    /// </summary>
    private Dictionary<string, JsonElement> ParseToolArguments(SkillStep step)
    {
        var arguments = new Dictionary<string, JsonElement>();

        if (!string.IsNullOrEmpty(step.Prompt))
        {
            var processedPrompt = ReplaceVariables(step.Prompt);

            var parsed = JsonArgumentParser.Parse(processedPrompt);
            if (parsed.Count > 0)
                return parsed;

            arguments["input"] = JsonSerializer.SerializeToElement(processedPrompt, SkillsJsonContext.Default.String);
            return arguments;
        }

        return arguments;
    }

    /// <summary>
    /// 从工具调用结果中提取文本内容
    /// </summary>
    private static string ExtractTextFromResult(ToolResult result)
    {
        if (result.Content == null || result.Content.Count == 0)
        {
            return string.Empty;
        }

        return string.Join("\n",
            result.Content
                .Select(content => !string.IsNullOrEmpty(content.Text) ? content.Text
                    : !string.IsNullOrEmpty(content.Data) ? content.Data
                    : null)
                .Where(t => t != null));
    }

    /// <summary>
    /// 执行提示步骤
    /// </summary>
    private async Task<string> ExecutePromptStepAsync(SkillStep step, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(step.Prompt))
        {
            throw new InvalidOperationException(ContractsErrorMessages.PromptStepMustSpecifyPrompt);
        }

        var prompt = ReplaceVariables(step.Prompt);

        var chatHistory = new JoinCode.Abstractions.LLM.Chat.MessageList();
        chatHistory.AddUserMessage(prompt);

        var responseBuilder = new System.Text.StringBuilder();

        await foreach (var chunk in _queryEngine.QueryAsync(prompt, chatHistory, cancellationToken))
        {
            if (chunk.Type == AgentStreamChunkType.Content)
            {
                responseBuilder.Append(chunk.Content);
            }
        }

        return responseBuilder.ToString();
    }

    /// <summary>
    /// 执行循环步骤
    /// </summary>
    private async Task<string> ExecuteLoopStepAsync(SkillStep step, CancellationToken cancellationToken)
    {
        if (step.Loop == null)
        {
            throw new InvalidOperationException(ContractsErrorMessages.LoopStepMustSpecifyLoopConfig);
        }

        var results = new List<string>();
        var iteration = 0;
        var maxIterations = step.Loop.Count ?? 10;

        while (iteration < maxIterations && !cancellationToken.IsCancellationRequested)
        {
            iteration++;
            _logger?.LogInformation("[SkillExecutor] 循环迭代 {Iteration}/{Max}", iteration, maxIterations);

            _variables["{{iteration}}"] = JsonSerializer.SerializeToElement(iteration, SkillsJsonContext.Default.Int32);

            var loopStep = new SkillStep
            {
                Id = $"{step.Id}_iteration_{iteration}",
                Type = SkillStepType.Prompt,
                Prompt = step.Prompt,
                Next = null
            };

            var result = await ExecuteStepAsync(loopStep, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess && result.Output != null)
            {
                results.Add(result.Output);
            }

            if (step.Loop.Condition != null)
            {
                var condition = ReplaceVariables(step.Loop.Condition);
                if (condition.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                    condition.Equals("0", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }

        return string.Join("\n", results);
    }

    /// <summary>
    /// 执行条件步骤
    /// </summary>
    private async Task<string> ExecuteConditionStepAsync(SkillStep step, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(step.Condition))
        {
            throw new InvalidOperationException(ContractsErrorMessages.ConditionStepMustSpecifyCondition);
        }

        var condition = ReplaceVariables(step.Condition);
        var isTrue = condition.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                     condition.Equals("1", StringComparison.OrdinalIgnoreCase);

        return isTrue ? L.T(StringKey.SkillExecutorConditionTrue) : L.T(StringKey.SkillExecutorConditionFalse);
    }

    /// <summary>
    /// 替换变量 - 支持嵌套变量、表达式和默认值
    /// </summary>
    /// <param name="input">输入字符串</param>
    /// <returns>替换后的字符串</returns>
    private string ReplaceVariables(string input)
    {
        return _variableResolver.Resolve(input, _variables, throwOnMissing: false);
    }

    /// <summary>
    /// 验证参数
    /// </summary>
    private string? ValidateParameters(SkillDefinition skill, Dictionary<string, JsonElement> parameters)
    {
        return skill.Parameters
            .Where(param => param.Value.Required && !parameters.ContainsKey(param.Key))
            .Select(param => L.T(StringKey.SkillExecutorMissingRequiredParam, param.Key))
            .FirstOrDefault();
    }
}
