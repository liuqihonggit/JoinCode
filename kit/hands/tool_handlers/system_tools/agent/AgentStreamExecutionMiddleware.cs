namespace Tools.Handlers;

/// <summary>
/// Agent 流式执行中间件 — 前台模式使用 RunAgentStreamAsync 流式执行子智能体
/// 对齐 TS runAgent AsyncGenerator
/// </summary>
[Register(typeof(IAgentToolMiddleware), ServiceLifetime.Singleton)]
public sealed partial class AgentStreamExecutionMiddleware : ServiceEntity, IAgentToolMiddleware {

    /// <summary>
    /// 构造 Agent 流式执行中间件
    /// </summary>
    /// <param name="agentService">代理服务，提供流式执行能力</param>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="telemetryService">可选遥测服务</param>
    /// <param name="outputChannelManager">可选子代理输出通道管理器</param>
    public AgentStreamExecutionMiddleware(IAgentService agentService, ILogger<AgentStreamExecutionMiddleware>? logger = null, ITelemetryService? telemetryService = null, JoinCode.Abstractions.Interfaces.IAgentOutputChannelManager? outputChannelManager = null) {
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
    public async Task InvokeAsync(AgentToolContext context, MiddlewareDelegate<AgentToolContext> next, CancellationToken ct) {
        var spawnOptions = context.SpawnOptions ?? new AgentSpawnOptions {
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

        void EmitStarted(string id) {
            channel?.Emit(JoinCode.Abstractions.LLM.Chat.ChatStreamEvent.AgentStarted(
                id,
                name: spawnOptions.Name ?? context.ResolvedPrimaryType,
                description: spawnOptions.Description,
                role: roleValue));
        }

        await foreach (var chunk in _agentService.RunAgentStreamAsync(spawnOptions, ct).ConfigureAwait(false)) {
            var isFirstChunk = agentId is null;
            agentId ??= chunk.AgentId;
            if (isFirstChunk && agentId is not null)
                EmitStarted(agentId);

            switch (chunk.Type) {
                case AgentStreamChunkType.Content:
                context.ContentBuilder.Append(chunk.Content);
                channel?.Emit(BuildChunkEvent(chunk, agentId)!);
                break;
                case AgentStreamChunkType.ThinkingStart:
                case AgentStreamChunkType.Thinking:
                {
                    var evt = BuildChunkEvent(chunk, agentId);
                    if (evt is not null)
                        channel?.Emit(evt);
                    break;
                }
                case AgentStreamChunkType.ToolCallStart:
                // 工具调用开始 — 对齐 TS onProgress({type:'agent_progress'})
                _logger?.LogDebug("[AgentStreamExecution] Agent {AgentId} calling tool: {ToolName}", chunk.AgentId, chunk.ToolName);
                channel?.Emit(BuildChunkEvent(chunk, agentId)!);
                break;
                case AgentStreamChunkType.ToolCallEnd:
                case AgentStreamChunkType.ToolProgress:
                channel?.Emit(BuildChunkEvent(chunk, agentId)!);
                break;
                case AgentStreamChunkType.Complete:
                context.ExecutionTimeMs = chunk.ExecutionTimeMs;
                // Complete 块的 Content 是最终输出，追加到响应
                if (chunk.Content is not null && succeeded) {
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

        if (agentId is not null) {
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

    /// <summary>
    /// 根据流式块构造对应的 ChatStreamEvent — 纯计算,无副作用
    /// </summary>
    /// <param name="chunk">流式块</param>
    /// <param name="agentId">代理 ID（首块后填充）</param>
    /// <returns>对应的事件;null 表示该块无需发射事件（Thinking 空内容、Complete/Error 由主流程处理状态）</returns>
    internal static JoinCode.Abstractions.LLM.Chat.ChatStreamEvent? BuildChunkEvent(AgentStreamChunk chunk, string? agentId) {
        return chunk.Type switch {
            AgentStreamChunkType.Content => new JoinCode.Abstractions.LLM.Chat.ChatStreamEvent {
                Type = JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.Content,
                Content = chunk.Content,
                AgentId = agentId
            },
            AgentStreamChunkType.ThinkingStart or AgentStreamChunkType.Thinking
                when !string.IsNullOrEmpty(chunk.ThinkingContent) || !string.IsNullOrEmpty(chunk.Content)
                => new JoinCode.Abstractions.LLM.Chat.ChatStreamEvent {
                Type = JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.Thinking,
                ThinkingContent = chunk.ThinkingContent ?? chunk.Content,
                AgentId = agentId
            },
            AgentStreamChunkType.ToolCallStart => new JoinCode.Abstractions.LLM.Chat.ChatStreamEvent {
                Type = JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.ToolCallStart,
                ToolName = chunk.ToolName,
                ToolCallId = chunk.ToolCallId,
                ToolArguments = chunk.ToolArguments,
                AgentId = agentId
            },
            AgentStreamChunkType.ToolCallEnd => new JoinCode.Abstractions.LLM.Chat.ChatStreamEvent {
                Type = JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.ToolCallEnd,
                ToolName = chunk.ToolName,
                ToolCallId = chunk.ToolCallId,
                ToolResultText = chunk.ToolResultText,
                IsToolError = chunk.IsToolError,
                StructuredPatch = chunk.StructuredPatch,
                AgentId = agentId
            },
            AgentStreamChunkType.ToolProgress => new JoinCode.Abstractions.LLM.Chat.ChatStreamEvent {
                Type = JoinCode.Abstractions.LLM.Chat.ChatStreamEventType.ToolProgress,
                ToolName = chunk.ToolName,
                ToolCallId = chunk.ToolCallId,
                ProgressType = chunk.ProgressType,
                ProgressMessage = chunk.ProgressMessage,
                AgentId = agentId
            },
            _ => null
        };
    }
}