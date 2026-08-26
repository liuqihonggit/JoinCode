namespace Tools.Handlers;

/// <summary>
/// Agent 流式执行中间件 — 前台模式使用 RunAgentStreamAsync 流式执行子智能体
/// 对齐 TS runAgent AsyncGenerator
/// </summary>
[Register(typeof(IAgentToolMiddleware), ServiceLifetime.Singleton)]
public sealed partial class AgentStreamExecutionMiddleware : ServiceEntity, IAgentToolMiddleware
{

    public AgentStreamExecutionMiddleware(IAgentService agentService, ILogger<AgentStreamExecutionMiddleware>? logger = null, ITelemetryService? telemetryService = null, JoinCode.Abstractions.Interfaces.IAgentOutputChannelManager? outputChannelManager = null)
    {
        _agentService = agentService;
        _logger = logger;
        _telemetryService = telemetryService;
        _outputChannelManager = outputChannelManager;
    }
    private readonly IAgentService _agentService;
    private readonly ILogger<AgentStreamExecutionMiddleware>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly JoinCode.Abstractions.Interfaces.IAgentOutputChannelManager? _outputChannelManager;

    /// <inheritdoc />
    public int Order => 400;

    /// <inheritdoc />

    /// <inheritdoc />
    public async Task InvokeAsync(AgentToolContext context, MiddlewareDelegate<AgentToolContext> next, CancellationToken ct)
    {
        var spawnOptions = context.SpawnOptions ?? new AgentSpawnOptions
        {
            Description = context.Description,
            Prompt = context.Prompt,
            Role = context.SubagentRole,
            Variant = context.SubagentVariant,
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
        string? finalOutput = null;
        JoinCode.Abstractions.LLM.Chat.TokenUsage? finalUsage = null;

        // GUI 多 subAgent 运行期显示的数据源：向主对话管道的子代理通道发射带身份事件。
        // 无通道时（CLI 纯文本等场景）SubAgentEventChannel.Current 为 null，发射自然跳过。
        var channel = JoinCode.Abstractions.LLM.Chat.SubAgentEventChannel.Current;
        var roleValue = spawnOptions.Role.ToValue();

        void EmitStarted(string id)
        {
            channel?.Emit(JoinCode.Abstractions.LLM.Chat.ChatStreamEvent.AgentStarted(
                id,
                name: spawnOptions.Name ?? context.ResolvedPrimaryType,
                description: spawnOptions.Description,
                role: roleValue));
        }

        await foreach (var chunk in _agentService.RunAgentStreamAsync(spawnOptions, ct).ConfigureAwait(false))
        {
            var isFirstChunk = agentId is null;
            agentId ??= chunk.AgentId;
            if (isFirstChunk && agentId is not null)
                EmitStarted(agentId);

            switch (chunk.Type)
            {
                case AgentStreamChunkType.Content:
                    context.ContentBuilder.Append(chunk.Content);
                    channel?.Emit(new JoinCode.Abstractions.LLM.Chat.ChatStreamEvent
                    {
                        Type = JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.Content,
                        Content = chunk.Content,
                        AgentId = agentId
                    });
                    break;
                case AgentStreamChunkType.ThinkingStart:
                case AgentStreamChunkType.Thinking:
                    if (!string.IsNullOrEmpty(chunk.ThinkingContent) || !string.IsNullOrEmpty(chunk.Content))
                    {
                        channel?.Emit(new JoinCode.Abstractions.LLM.Chat.ChatStreamEvent
                        {
                            Type = JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.Thinking,
                            ThinkingContent = chunk.ThinkingContent ?? chunk.Content,
                            AgentId = agentId
                        });
                    }
                    break;
                case AgentStreamChunkType.ToolCallStart:
                    // 工具调用开始 — 对齐 TS onProgress({type:'agent_progress'})
                    _logger?.LogDebug("[AgentStreamExecution] Agent {AgentId} calling tool: {ToolName}", chunk.AgentId, chunk.ToolName);
                    channel?.Emit(new JoinCode.Abstractions.LLM.Chat.ChatStreamEvent
                    {
                        Type = JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.ToolCallStart,
                        ToolName = chunk.ToolName,
                        ToolCallId = chunk.ToolCallId,
                        ToolArguments = chunk.ToolArguments,
                        AgentId = agentId
                    });
                    break;
                case AgentStreamChunkType.ToolCallEnd:
                    channel?.Emit(new JoinCode.Abstractions.LLM.Chat.ChatStreamEvent
                    {
                        Type = JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.ToolCallEnd,
                        ToolName = chunk.ToolName,
                        ToolCallId = chunk.ToolCallId,
                        ToolResultText = chunk.ToolResultText,
                        IsToolError = chunk.IsToolError,
                        StructuredPatch = chunk.StructuredPatch,
                        AgentId = agentId
                    });
                    break;
                case AgentStreamChunkType.ToolProgress:
                    channel?.Emit(new JoinCode.Abstractions.LLM.Chat.ChatStreamEvent
                    {
                        Type = JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.ToolProgress,
                        ToolName = chunk.ToolName,
                        ToolCallId = chunk.ToolCallId,
                        ProgressType = chunk.ProgressType,
                        ProgressMessage = chunk.ProgressMessage,
                        AgentId = agentId
                    });
                    break;
                case AgentStreamChunkType.Complete:
                    context.ExecutionTimeMs = chunk.ExecutionTimeMs;
                    // Complete 块的 Content 是最终输出，追加到响应
                    if (chunk.Content is not null && succeeded)
                    {
                        context.ContentBuilder.Append(chunk.Content);
                        finalOutput = chunk.Content;
                    }
                    finalUsage = chunk.Usage;
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

        if (agentId is not null)
        {
            // 统计收尾：成功携带最终输出（Complete 块），失败携带错误消息
            channel?.Emit(JoinCode.Abstractions.LLM.Chat.ChatStreamEvent.AgentFinished(
                agentId,
                success: succeeded,
                executionTimeMs: context.ExecutionTimeMs,
                usage: finalUsage,
                finalOutput: succeeded ? finalOutput : errorMessage));
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
