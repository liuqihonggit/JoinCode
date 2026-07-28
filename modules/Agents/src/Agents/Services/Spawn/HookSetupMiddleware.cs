namespace Core.Agents;

/// <summary>
/// Hook 注册中间件 — 注册 Agent 定义中的 Hooks
/// </summary>
[Register]
public sealed partial class HookSetupMiddleware : IAgentSpawnMiddleware
{
    [Inject] private readonly ISessionHookManager? _sessionHookManager;
    [Inject] private readonly ILogger<HookSetupMiddleware>? _logger;

    /// <summary>Hook 注册在 Spawn 之后</summary>

    /// <summary>Hook 注册失败不应中断管道</summary>
    public JoinCode.Abstractions.Pipeline.ErrorBehavior OnError => JoinCode.Abstractions.Pipeline.ErrorBehavior.Continue;

    public async Task InvokeAsync(AgentSpawnContext context, JoinCode.Abstractions.Pipeline.MiddlewareDelegate<AgentSpawnContext> next, CancellationToken ct)
    {
        if (_sessionHookManager is not null && context.SubAgent is not null)
        {
            await RegisterAgentHooksAsync(context.SubAgent.Id, context.Definition, ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task RegisterAgentHooksAsync(string agentId, JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? definition, CancellationToken cancellationToken)
    {
        if (definition?.Hooks is null || definition.Hooks.Count == 0)
            return;

        var sessionId = "default";

        try
        {
            foreach (var (eventName, matchers) in definition.Hooks)
            {
                var hookEvent = HookEventExtensions.FromValue(eventName);
                if (hookEvent is not { } evt)
                {
                    _logger?.LogWarning("[HookSetupMiddleware] 未知 HookEvent: {EventName}, 跳过", eventName);
                    continue;
                }

                if (evt == HookEvent.Stop)
                    evt = HookEvent.SubagentStop;

                var hookTasks = new List<Task>();
                foreach (var matcher in matchers)
                {
                    foreach (var hookCmd in matcher.Hooks)
                    {
                        var hookCommand = ConvertToHookCommand(hookCmd);
                        if (hookCommand is null) continue;

                        hookTasks.Add((_sessionHookManager ?? throw new InvalidOperationException("SessionHookManager not available")).AddSessionHookAsync(
                            sessionId, evt, matcher.Matcher, hookCommand, cancellationToken));
                    }
                }

                if (hookTasks.Count > 0)
                {
                    await Task.WhenAll(hookTasks).ConfigureAwait(false);
                }
            }

            _logger?.LogInformation("[HookSetupMiddleware] 已为 Agent {AgentId} 注册 {Count} 个 Hook 事件",
                agentId, definition.Hooks.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[HookSetupMiddleware] 注册 Agent {AgentId} Hooks 失败", agentId);
        }
    }

    private static HookCommand? ConvertToHookCommand(JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookCommand cmd)
    {
        return cmd.Type.ToLowerInvariant() switch
        {
            "command" when !string.IsNullOrWhiteSpace(cmd.Command) => new BashCommandHook
            {
                Command = cmd.Command,
                If = cmd.If,
                Timeout = cmd.Timeout
            },
            "prompt" when !string.IsNullOrWhiteSpace(cmd.Prompt) => new PromptHook
            {
                Prompt = cmd.Prompt,
                If = cmd.If,
                Timeout = cmd.Timeout
            },
            _ => null
        };
    }
}
