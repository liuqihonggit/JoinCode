
namespace Core.Hooks.Execution;

/// <summary>
/// 代理验证钩子执行器
/// 使用专门的验证代理进行工具调用验证
/// </summary>
[Register(typeof(IHookExecutor))]
public sealed class AgentHookExecutor : HookExecutorBase<AgentHook>
{
    private readonly IAgentService? _agentService;

    public AgentHookExecutor(
        IAgentService? agentService = null,
        ILogger<AgentHookExecutor>? logger = null)
        : base(logger)
    {
        _agentService = agentService;
    }

    /// <inheritdoc />
    public override string SupportedType => HookTypeConstants.Agent;

    /// <inheritdoc />
    public override async Task<HookResult> ExecuteTypedAsync(
        AgentHook hook,
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
                ct => ExecuteAgentAsync(hook, processedPrompt, input, ct),
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
            Logger?.LogError(ex, "Failed to execute agent hook");
            return HookResult.NonBlockingError(
                error: ex.Message,
                message: $"Agent hook execution failed: {ex.Message}");
        }
    }

    private async Task<HookResult> ExecuteAgentAsync(
        AgentHook hook,
        string prompt,
        HookInput input,
        CancellationToken cancellationToken)
    {
        // 构建验证代理的完整提示
        var fullPrompt = BuildAgentPrompt(prompt, input);

        // 使用小型快速模型（如果未指定）
        var model = hook.Model ?? JoinCode.Abstractions.Configuration.Llm.ModelConfigLoader.GetDefaultModelId("anthropic");

        // 调用代理服务 — 未注册时返回非阻塞错误（不阻断 DI 链路）
        if (_agentService is null)
        {
            return HookResult.NonBlockingError("IAgentService 未注册，代理验证钩子不可用");
        }

        var response = await _agentService.RunAsync(
            fullPrompt,
            model,
            maxTokens: 500, // 限制 token 使用量
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // 解析代理响应
        return ParseAgentResponse(response, input);
    }

    private string BuildAgentPrompt(string userPrompt, HookInput input)
    {
        var payloadJson = JsonSerializer.Serialize(input.Payload, HooksJsonContext.Default.DictionaryStringJsonElement);

        return $$$"""
            You are a specialized validation agent. Your task is to validate a tool call.

            Event Type: {{input.Event}}
            Tool Name: {{input.ToolName ?? "N/A"}}
            Tool Use ID: {{input.ToolUseId ?? "N/A"}}

            Input Parameters:
            ```json
            {{payloadJson}}
            ```

            Validation Instructions:
            {{userPrompt}}

            You must respond with a JSON object containing:
            {
                "decision": "allow" | "block" | "ask",
                "reason": "detailed explanation",
                "continue": true | false,
                "confidence": 0.0 to 1.0
            }

            Decision guidelines:
            - "allow": The tool call is safe and should proceed
            - "block": The tool call is dangerous or violates policies
            - "ask": Need user confirmation before proceeding
            """;
    }

    private HookResult ParseAgentResponse(AgentResponse response, HookInput input)
    {
        if (!response.Success)
        {
            Logger?.LogWarning("Agent execution failed: {Error}", response.Error);
            return HookResult.NonBlockingError(
                error: response.Error ?? "Agent execution failed",
                message: "Validation agent failed, allowing by default");
        }

        // 提取 JSON 响应
        var jsonContent = ExtractJsonFromResponse(response.Content);

        if (string.IsNullOrEmpty(jsonContent))
        {
            Logger?.LogWarning("Agent response did not contain valid JSON");
            return HookResult.Success(message: "Agent validation passed (no JSON)");
        }

        try
        {
            var hookDecision = JsonSerializer.Deserialize(jsonContent, HooksJsonContext.Default.HookDecision);
            if (hookDecision is null)
                return HookResult.Success(message: "Agent validation passed (empty response)");

            var decisionStr = hookDecision.Decision?.ToLowerInvariant() ?? PermissionBehaviorConstants.Allow;
            var decision = PermissionBehaviorExtensions.FromValue(decisionStr) ?? PermissionBehavior.Allow;
            var reason = hookDecision.Reason;
            var shouldContinue = hookDecision.Continue ?? true;
            var confidence = hookDecision.Confidence ?? 1.0;

            Logger?.LogDebug(
                "Agent decision: {Decision} (confidence: {Confidence:F2})",
                decisionStr,
                confidence);

            return decision switch
            {
                PermissionBehavior.Block when confidence > 0.7 => HookResult.Blocking(
                    error: reason ?? "Blocked by validation agent",
                    command: input.ToolName ?? "unknown",
                    message: $"[Confidence: {confidence:F0%}] {reason}"),

                PermissionBehavior.Block => HookResult.NonBlockingError(
                    error: reason ?? "Validation agent warning",
                    message: $"[Low confidence block] {reason}"),

                PermissionBehavior.Ask => new HookResult
                {
                    Outcome = HookOutcome.Success,
                    Message = reason,
                    PreventContinuation = !shouldContinue,
                    AdditionalContext = $"Agent suggests asking user (confidence: {confidence:F0%})"
                },

                _ => HookResult.Success(
                    message: reason ?? "Agent validation passed",
                    additionalContext: $"Confidence: {confidence:F0%}")
            };
        }
        catch (JsonException ex)
        {
            Logger?.LogWarning(ex, "Failed to parse agent JSON response");
            return HookResult.Success(
                message: "Agent validation passed",
                additionalContext: $"Raw response: {response.Content?[..Math.Min(response.Content.Length, 100)]}");
        }
    }

}

/// <summary>
/// 代理服务接口
/// </summary>
public interface IAgentService
{
    /// <summary>
    /// 运行代理
    /// </summary>
    Task<AgentResponse> RunAsync(
        string prompt,
        string model,
        int? maxTokens = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 代理响应
/// </summary>
public sealed record AgentResponse
{
    public required bool Success { get; init; }
    public string? Content { get; init; }
    public string? Error { get; init; }
    public int? TokensUsed { get; init; }
    public TimeSpan? Duration { get; init; }
}
