namespace Core.Context;

/// <summary>
/// 工具执行处理器接口 — 工具调用准备、执行、结果持久化
/// </summary>
public interface IToolExecutionHandler
{
    /// <summary>
    /// 准备工具调用列表 — 处理向后兼容的单工具调用场景
    /// </summary>
    IReadOnlyList<ToolCallEntry> PrepareToolCalls(IterationState iterState);

    /// <summary>
    /// 执行工具调用并应用 ContextModifier 和消息注入
    /// </summary>
    Task<ToolCallResult> ExecuteToolCallAsync(
        string toolName, string? toolCallId, Dictionary<string, JsonElement>? arguments,
        ChatMiddlewareContext context, CancellationToken ct);

    /// <summary>
    /// 将工具调用结果持久化到上下文：处理超大结果替换、添加结果消息、应用预算控制
    /// </summary>
    Task ApplyToolResultToContextAsync(
        string toolName, string? toolCallId, string? toolResultText,
        bool toolError, IReadOnlyList<ToolContent>? toolContentBlocks,
        ChatMiddlewareContext context, CancellationToken ct);

    /// <summary>
    /// 为被中断的工具调用批量写入占位结果，避免孤立 tool_call 导致下一轮 LLM API 400
    /// </summary>
    Task WriteAbortedToolResultsAsync(
        IReadOnlyList<ToolCallEntry> toolCalls, int startIndex, CancellationToken ct);
}

/// <summary>
/// 工具执行处理器 — 封装工具调用执行、ContextModifier应用、消息注入、结果持久化
/// </summary>
[Register(typeof(IToolExecutionHandler))]
public sealed partial class ToolExecutionHandler : IToolExecutionHandler
{
    private readonly IChatToolOrchestrator _toolOrchestrator;
    private readonly IChatContextManager _contextManager;
    private readonly QueryLoopServices? _services;
    [Inject] private readonly ILogger<ToolExecutionHandler>? _logger;

    public ToolExecutionHandler(
        IChatToolOrchestrator toolOrchestrator,
        IChatContextManager contextManager,
        QueryLoopServices? services = null,
        ILogger<ToolExecutionHandler>? logger = null)
    {
        _toolOrchestrator = toolOrchestrator;
        _contextManager = contextManager;
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ToolCallEntry> PrepareToolCalls(IterationState iterState)
    {
        var toolCalls = iterState.ToolCalls;
        if (toolCalls.Count > 0)
            return toolCalls;

        // 向后兼容：ToolCalls 列表为空但 ToolCallName 不为空时，用单个工具调用填充
        var fallbackArgsJson = iterState.ToolCallArguments is not null
            ? JsonSerializer.Serialize(iterState.ToolCallArguments, ChatServiceJsonContext.Default.DictionaryStringJsonElement)
            : "{}";
        return [new() { Id = iterState.ToolCallId, Name = iterState.ToolCallName ?? string.Empty, Arguments = fallbackArgsJson }];
    }

    /// <inheritdoc/>
    public async Task<ToolCallResult> ExecuteToolCallAsync(
        string toolName, string? toolCallId, Dictionary<string, JsonElement>? arguments,
        ChatMiddlewareContext context, CancellationToken cancellationToken)
    {
        ToolCallResult toolCallResult;
        try
        {
            toolCallResult = await _toolOrchestrator.ExecuteToolCallAsync(
                toolName, toolCallId, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (PermissionPendingConfirmationException ex)
        {
            _logger?.LogWarning("[ToolExecutionHandler] 工具权限待确认，交互模式下作为拒绝返回给AI: {ToolName}", toolName);
            toolCallResult = new ToolCallResult { ResultText = $"Error: {ex.Message}", IsError = true };
        }

        // 应用 ContextModifier
        if (toolCallResult.ContextModifier is not null)
        {
            toolCallResult.ContextModifier(context.ToolUseContext);
            _logger?.LogInformation("[ToolExecutionHandler] ContextModifier 已应用: AllowedTools={Count}, Model={Model}, Effort={Effort}",
                context.ToolUseContext.AllowedTools.Count, context.ToolUseContext.ModelOverride, context.ToolUseContext.Effort);
        }

        // 注入消息
        if (toolCallResult.InjectedMessages is not null && toolCallResult.InjectedMessages.Count > 0)
        {
            foreach (var injectedMsg in toolCallResult.InjectedMessages)
            {
                if (injectedMsg.Role == MessageRole.User)
                {
                    await _contextManager.AddUserMessageAsync(injectedMsg.Content ?? string.Empty, cancellationToken).ConfigureAwait(false);
                }
                else if (injectedMsg.Role == MessageRole.Assistant)
                {
                    await _contextManager.AddAssistantMessageAsync(injectedMsg.Content ?? string.Empty, cancellationToken).ConfigureAwait(false);
                }
            }
            _logger?.LogInformation("[ToolExecutionHandler] InjectedMessages 已注入: {Count} 条消息", toolCallResult.InjectedMessages.Count);
        }

        return toolCallResult;
    }

    /// <inheritdoc/>
    public async Task ApplyToolResultToContextAsync(
        string toolName, string? toolCallId, string? toolResultText,
        bool toolError, IReadOnlyList<ToolContent>? toolContentBlocks,
        ChatMiddlewareContext context, CancellationToken cancellationToken)
    {
        // 超大工具结果持久化
        var effectiveToolResult = toolResultText;
        if (!toolError && !string.IsNullOrEmpty(toolResultText))
        {
            var sessionId = (_contextManager is ChatContextManager cm) ? cm.SessionId : "default";
            var replacement = _services?.ContentReplacer?.MaybePersistLargeToolResult(
                toolName, toolCallId ?? string.Empty, toolResultText, sessionId);
            if (replacement is not null)
            {
                effectiveToolResult = replacement;
            }
        }

        var toolMetadata = ToolCallEntry.BuildToolResultMetadata(toolCallId, toolName);

        await _contextManager.AddToolResultMessageAsync(effectiveToolResult ?? string.Empty, toolMetadata, toolContentBlocks, cancellationToken).ConfigureAwait(false);

        // per-message 预算控制
        if (context.ToolUseContext.ContentReplacementState is not null && _services?.ContentReplacer is not null)
        {
            var sessionId = (_contextManager is ChatContextManager cm) ? cm.SessionId : "default";
            var messageList = await _contextManager.GetMessageListAsync(cancellationToken).ConfigureAwait(false);
            var budgetResult = await _services.ContentReplacer.ApplyBudgetAsync(
                messageList, context.ToolUseContext.ContentReplacementState, sessionId,
                cancellationToken).ConfigureAwait(false);

            if (budgetResult.BudgetedMessages is not null)
            {
                messageList.ReplaceAll(budgetResult.BudgetedMessages);
            }
        }
    }

    /// <inheritdoc/>
    public async Task WriteAbortedToolResultsAsync(
        IReadOnlyList<ToolCallEntry> toolCalls, int startIndex, CancellationToken cancellationToken)
    {
        for (var i = startIndex; i < toolCalls.Count; i++)
        {
            var placeholderMetadata = ToolCallEntry.BuildToolResultMetadata(toolCalls[i].Id, toolCalls[i].Name);
            await _contextManager.AddToolResultMessageAsync(
                "(aborted)", placeholderMetadata, null, cancellationToken).ConfigureAwait(false);
        }
    }
}
