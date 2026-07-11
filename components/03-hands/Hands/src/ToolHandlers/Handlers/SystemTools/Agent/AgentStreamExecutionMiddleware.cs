namespace Tools.Handlers;

/// <summary>
/// Agent 流式执行中间件 — 前台模式使用 RunAgentStreamAsync 流式执行子智能体
/// 对齐 TS runAgent AsyncGenerator
/// </summary>
[Register]
public sealed partial class AgentStreamExecutionMiddleware : IAgentToolMiddleware
{
    [Inject] private readonly IAgentService _agentService;
    [Inject] private readonly ILogger<AgentStreamExecutionMiddleware>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;

    /// <inheritdoc />
    public int Order => 400;

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <inheritdoc />
    public async Task InvokeAsync(AgentToolContext context, MiddlewareDelegate<AgentToolContext> next, CancellationToken ct)
    {
        var spawnOptions = context.SpawnOptions ?? new AgentSpawnOptions
        {
            Description = context.Description,
            Prompt = context.Prompt,
            AgentType = context.SubagentType,
            RunInBackground = false,
            IsolationMode = AgentIsolationModeExtensions.FromValue(context.Isolation) ?? AgentIsolationMode.None,
            MemoryScope = AgentMemoryScopeExtensions.FromValue(context.Memory),
            Model = context.Model,
            Name = context.Name,
            Cwd = context.Cwd
        };

        // 前台模式: 使用流式执行 — 对齐 TS runAgent AsyncGenerator
        string? agentId = null;
        var succeeded = true;
        string? errorMessage = null;

        await foreach (var chunk in _agentService.RunAgentStreamAsync(spawnOptions, ct).ConfigureAwait(false))
        {
            agentId ??= chunk.AgentId;

            switch (chunk.Type)
            {
                case AgentStreamChunkType.Content:
                    context.ContentBuilder.Append(chunk.Content);
                    break;
                case AgentStreamChunkType.ToolCallStart:
                    // 工具调用开始 — 对齐 TS onProgress({type:'agent_progress'})
                    _logger?.LogDebug("[AgentStreamExecution] Agent {AgentId} calling tool: {ToolName}", chunk.AgentId, chunk.ToolName);
                    break;
                case AgentStreamChunkType.Complete:
                    context.ExecutionTimeMs = chunk.ExecutionTimeMs;
                    // Complete 块的 Content 是最终输出，追加到响应
                    if (chunk.Content is not null && succeeded)
                    {
                        context.ContentBuilder.Append(chunk.Content);
                    }
                    break;
                case AgentStreamChunkType.Error:
                    succeeded = false;
                    errorMessage = chunk.Content;
                    break;
            }
        }

        context.AgentId = agentId;
        context.Succeeded = succeeded;
        context.ErrorMessage = errorMessage;

        await next(context, ct).ConfigureAwait(false);
    }
}
