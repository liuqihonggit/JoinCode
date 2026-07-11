
namespace Core.Hooks.Execution;

/// <summary>
/// LLM 提示钩子执行器
/// 使用 LLM 验证工具调用
/// </summary>
[Register(typeof(IHookExecutor))]
public sealed class PromptHookExecutor : HookExecutorBase<PromptHook>
{
    private readonly ILLMService? _llmService;

    public PromptHookExecutor(
        ILLMService? llmService = null,
        ILogger<PromptHookExecutor>? logger = null)
        : base(logger)
    {
        _llmService = llmService;
    }

    /// <inheritdoc />
    public override string SupportedType => HookTypeConstants.Prompt;

    /// <inheritdoc />
    public override async Task<HookResult> ExecuteTypedAsync(
        PromptHook hook,
        HookInput input,
        CancellationToken cancellationToken = default)
    {
        LogExecutionStart(hook, input);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var context = CreateContext(hook, input);
            var inputJson = PrepareInputJson(input);
            var processedPrompt = SubstituteArguments(hook.Prompt, inputJson);

            var result = await ExecuteWithTimeoutAsync(
                ct => ExecutePromptAsync(hook, processedPrompt, input, ct),
                context.Timeout,
                hook.GetDisplayText(),
                cancellationToken).ConfigureAwait(false);

            LogExecutionComplete(hook, result, stopwatch.Elapsed);
            return result;
        }
        catch (HookTimeoutException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to execute prompt hook");
            return HookResult.NonBlockingError(
                error: ex.Message,
                message: $"Prompt hook execution failed: {ex.Message}");
        }
    }

    private async Task<HookResult> ExecutePromptAsync(
        PromptHook hook,
        string prompt,
        HookInput input,
        CancellationToken cancellationToken)
    {
        // ILLMService 未注册时返回非阻塞错误
        if (_llmService is null)
        {
            return HookResult.NonBlockingError("ILLMService 未注册，提示钩子执行器不可用");
        }

        // 构建系统提示
        var systemPrompt = BuildSystemPrompt(input);

        // 调用 LLM
        var response = await _llmService.CompleteAsync(
            prompt,
            systemPrompt,
            hook.Model,
            cancellationToken).ConfigureAwait(false);

        // 解析响应
        return ParseLLMResponse(response, input);
    }

    private string BuildSystemPrompt(HookInput input)
    {
        return $"""
            You are a security validator for tool calls. 
            Event: {input.Event}
            Tool: {input.ToolName ?? "N/A"}
            
            Analyze the tool call and respond with a JSON object:
            - "decision": "allow" | "block" | "ask"
            - "reason": explanation of your decision
            - "continue": true | false (whether to continue execution)
            
            For "ask" decisions, include:
            - "message": question to ask the user
            """;
    }

    private HookResult ParseLLMResponse(string response, HookInput input)
    {
        // 尝试提取 JSON
        var jsonMatch = ExtractJsonFromResponse(response);

        if (string.IsNullOrEmpty(jsonMatch))
        {
            Logger?.LogWarning("LLM response did not contain valid JSON: {Response}", response[..Math.Min(response.Length, 100)]);
            return HookResult.Success(message: "LLM validation passed (no JSON response)");
        }

        try
        {
            var hookDecision = JsonSerializer.Deserialize(jsonMatch, HooksJsonContext.Default.HookDecision);
            if (hookDecision is null)
                return HookResult.Success(message: "LLM validation passed (empty response)");

            var decisionStr = hookDecision.Decision?.ToLowerInvariant() ?? PermissionBehaviorConstants.Allow;
            var decision = PermissionBehaviorExtensions.FromValue(decisionStr) ?? PermissionBehavior.Allow;
            var reason = hookDecision.Reason;
            var shouldContinue = hookDecision.Continue ?? true;

            return decision switch
            {
                PermissionBehavior.Block => HookResult.Blocking(
                    error: reason ?? "Blocked by LLM validation",
                    command: input.ToolName ?? "unknown",
                    message: reason),

                PermissionBehavior.Ask => new HookResult
                {
                    Outcome = HookOutcome.Success,
                    Message = hookDecision.Message ?? reason,
                    PreventContinuation = !shouldContinue
                },

                _ => HookResult.Success(
                    message: reason ?? "LLM validation passed",
                    additionalContext: reason)
            };
        }
        catch (JsonException ex)
        {
            Logger?.LogWarning(ex, "Failed to parse LLM JSON response");
            return HookResult.Success(message: "LLM validation passed (JSON parse failed)");
        }
    }

}

/// <summary>
/// LLM 服务接口
/// </summary>
public interface ILLMService
{
    /// <summary>
    /// 完成提示
    /// </summary>
    Task<string> CompleteAsync(
        string prompt,
        string? systemPrompt = null,
        string? model = null,
        CancellationToken cancellationToken = default);
}
