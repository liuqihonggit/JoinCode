namespace McpToolRegistry;

[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal sealed partial class HookMiddlewareJsonContext : JsonSerializerContext;

/// <summary>
/// PreToolUse Hook 中间件 — 工具执行前触发 HookEvent.PreToolUse
/// 对齐 ChatToolOrchestrator.ExecutePreHooksAsync
/// </summary>
[Register]
public sealed partial class PreToolUseHookMiddleware : ServiceEntity, IToolExecutionMiddleware
{
    private readonly IHookOrchestrator? _hookOrchestrator;
    [Inject] private readonly ILogger<PreToolUseHookMiddleware>? _logger;

    public PreToolUseHookMiddleware(
        IHookOrchestrator? hookOrchestrator = null,
        ILogger<PreToolUseHookMiddleware>? logger = null)
    {
        _hookOrchestrator = hookOrchestrator;
        _logger = logger;
    }

    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct)
    {
        if (_hookOrchestrator is not null)
        {
            var prePayload = new Dictionary<string, JsonElement>
            {
                ["tool_name"] = JsonSerializer.SerializeToElement(context.ToolName),
                ["tool_input"] = JsonSerializer.SerializeToElement(context.Arguments, HookMiddlewareJsonContext.Default.DictionaryStringJsonElement),
            };

            await foreach (var hookResult in _hookOrchestrator.ExecuteHooksAsync(
                HookEvent.PreToolUse, prePayload, matcher: context.ToolName, cancellationToken: ct).ConfigureAwait(false))
            {
                if (hookResult.Outcome == HookOutcome.Blocking)
                {
                    _logger?.LogInformation("[PreToolUseHook] Hook 阻止工具执行: {ToolName}, Message={Message}", context.ToolName, hookResult.Message);
                    throw PermissionDeniedException.ToolDenied(context.ToolName, hookResult.Message ?? "Hook 阻止了工具执行");
                }
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// PostToolUse Hook 中间件 — 工具执行后触发 HookEvent.PostToolUse
/// 对齐 ChatToolOrchestrator.ExecutePostHooksAsync
/// 放在 ToolExecutionMiddleware 之前，用 await next 后置模式读取 context.Result
/// </summary>
[Register]
public sealed partial class PostToolUseHookMiddleware : ServiceEntity, IToolExecutionMiddleware
{
    private readonly IHookOrchestrator? _hookOrchestrator;
    [Inject] private readonly ILogger<PostToolUseHookMiddleware>? _logger;

    public PostToolUseHookMiddleware(
        IHookOrchestrator? hookOrchestrator = null,
        ILogger<PostToolUseHookMiddleware>? logger = null)
    {
        _hookOrchestrator = hookOrchestrator;
        _logger = logger;
    }

    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct)
    {
        await next(context, ct).ConfigureAwait(false);

        if (_hookOrchestrator is not null && context.Result is not null)
        {
            var resultText = string.Join("\n", context.Result.Content.Select(c => c.Text ?? string.Empty));
            var postPayload = new Dictionary<string, JsonElement>
            {
                ["tool_name"] = JsonSerializer.SerializeToElement(context.ToolName),
                ["tool_result"] = JsonSerializer.SerializeToElement(resultText),
            };

            await foreach (var _ in _hookOrchestrator.ExecuteHooksAsync(
                HookEvent.PostToolUse, postPayload, matcher: context.ToolName, cancellationToken: ct).ConfigureAwait(false))
            {
            }
        }
    }
}
