namespace Core.Context;

/// <summary>
/// Mock 预处理中间件 — 跳过所有预处理逻辑，设置必要的上下文后调用下游
/// 用于测试场景，隔离预处理依赖
/// </summary>
public sealed class MockPreChatMiddleware : IChatMiddleware
{
    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // 设置 PromptSnapshot 以便 CleanupAsync 正常执行 ProcessUsageAsync
        context.PromptSnapshot = new PromptStateSnapshot
        {
            SystemPromptHash = "mock",
            ToolSpecsHash = "mock",
            ToolCount = 0,
            ToolNamesHash = "mock",
            DynamicContentHash = "mock"
        };

        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }
    }
}
