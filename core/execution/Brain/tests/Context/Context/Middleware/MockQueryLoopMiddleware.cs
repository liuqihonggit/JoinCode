namespace Core.Context;

/// <summary>
/// Mock 查询循环中间件 — 支持脚本驱动或固定文本响应
/// 用于测试场景，隔离 LLM 和工具依赖
/// </summary>
public sealed class MockQueryLoopMiddleware : IChatMiddleware
{
    private readonly string? _fixedResponse;
    private readonly IReadOnlyList<MockScriptEntry>? _scriptTurns;
    private int _scriptTurnIndex;

    /// <summary>
    /// 固定文本响应模式 — 每次调用返回相同文本
    /// </summary>
    public MockQueryLoopMiddleware(string response = "Mock response")
    {
        _fixedResponse = response;
    }

    /// <summary>
    /// 脚本驱动模式 — 按轮次返回预设响应
    /// </summary>
    public MockQueryLoopMiddleware(IReadOnlyList<MockScriptEntry> scriptTurns)
    {
        _scriptTurns = scriptTurns;
    }

    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var turn = GetCurrentTurn();

        if (turn.ThinkingContent is not null)
        {
            yield return ChatStreamEvent.Thinking(turn.ThinkingContent);
        }

        if (turn.ToolCalls is { Count: > 0 })
        {
            foreach (var tc in turn.ToolCalls)
            {
                var callId = tc.ToolCallId ?? $"call-{Guid.NewGuid():N}";
                yield return ChatStreamEvent.ToolStart(tc.ToolName, callId, tc.Arguments);
                yield return ChatStreamEvent.ToolEnd(tc.ToolName, tc.Result ?? $"[Mock] {tc.ToolName} result", callId);
            }
        }

        if (!string.IsNullOrEmpty(turn.TextResponse))
        {
            yield return ChatStreamEvent.Text(turn.TextResponse);
        }

        context.TotalToolCalls = turn.ToolCalls?.Count ?? 0;
        context.FinalUsage = new TokenUsage(10, 20);
        context.FinalModelId = "mock-model";

        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }

        yield return ChatStreamEvent.Done(context.FinalUsage, context.FinalModelId);
    }

    private MockScriptEntry GetCurrentTurn()
    {
        if (_scriptTurns is null || _scriptTurnIndex >= _scriptTurns.Count)
        {
            return new MockScriptEntry
            {
                TextResponse = _fixedResponse ?? "脚本已耗尽，无更多回复。"
            };
        }

        return _scriptTurns[_scriptTurnIndex++];
    }
}

/// <summary>
/// Mock 脚本轮次 — 用于 MockQueryLoopMiddleware 的脚本驱动模式
/// </summary>
public sealed record MockScriptEntry
{
    public required string TextResponse { get; init; }
    public IReadOnlyList<MockToolCallEntry>? ToolCalls { get; init; }
    public string? ThinkingContent { get; init; }
}

/// <summary>
/// Mock 工具调用条目
/// </summary>
public sealed record MockToolCallEntry
{
    public required string ToolName { get; init; }
    public required string Arguments { get; init; }
    public string? ToolCallId { get; init; }
    public string? Result { get; init; }
}
